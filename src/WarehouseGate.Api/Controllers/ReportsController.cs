using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

// Detailed operational reporting for the portal roles: every Inward/Outward transaction in a
// date range with full journey timestamps, derived durations, quantities and exceptions -
// warehouse-scoped exactly like the dashboard endpoints (see WarehouseScopeResolver).
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "SuperAdmin,LogisticsManager,Office")]
public class ReportsController : ControllerBase
{
    private const int MaxRangeDays = 366;

    private readonly WarehouseGateDbContext _db;
    private readonly WarehouseScopeResolver _scopeResolver;

    public ReportsController(WarehouseGateDbContext db, WarehouseScopeResolver scopeResolver)
    {
        _db = db;
        _scopeResolver = scopeResolver;
    }

    // A transaction belongs to the range by its creation event (Inward = gate-in, Outward =
    // pick-list creation), regardless of current status - detailed reporting covers in-flight
    // work too, not just completed jobs. Dates are inclusive calendar days (UTC).
    [HttpGet("detailed")]
    public async Task<ActionResult<DetailedReportDto>> GetDetailed([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var fromDate = from ?? toDate.AddDays(-29);

        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        if (toDate.DayNumber - fromDate.DayNumber > MaxRangeDays)
        {
            return BadRequest(new { message = $"Date range cannot exceed {MaxRangeDays} days." });
        }

        var rangeStart = fromDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var warehouseIdScope = await _scopeResolver.ResolveAsync(User);

        var inwardQuery = _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.Warehouse)
            .Include(t => t.PurchaseOrder)
            .Include(t => t.InspectionLines)
            .Include(t => t.Grn)
            .Where(t => t.GateInTime >= rangeStart && t.GateInTime < rangeEnd);
        var outwardQuery = _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.Warehouse)
            .Include(t => t.DispatchOrder)
            .Include(t => t.LoadLines)
            .Include(t => t.DispatchNote)
            .Where(t => t.CreatedTime >= rangeStart && t.CreatedTime < rangeEnd);

        if (warehouseIdScope is not null)
        {
            inwardQuery = inwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
            outwardQuery = outwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
        }

        var inward = await inwardQuery.OrderByDescending(t => t.GateInTime).AsSplitQuery().ToListAsync();
        var outward = await outwardQuery.OrderByDescending(t => t.CreatedTime).AsSplitQuery().ToListAsync();

        var supervisorIds = inward.Select(t => t.AssignedSupervisorUserId)
            .Concat(outward.Select(t => t.AssignedSupervisorUserId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var supervisorNames = await _db.Users
            .Where(u => supervisorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        string SupervisorName(string? id) =>
            id is not null && supervisorNames.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "—";

        var inwardRows = inward.Select(t =>
        {
            var processingStart = t.UnloadingStartTime ?? t.DockInTime;
            var processingEnd = t.DockOutTime ?? t.GateOutTime;
            return new InwardReportRowDto(
                t.Id,
                t.InwardTxnNumber,
                t.Vehicle?.Number ?? "—",
                t.PurchaseOrder?.PONumber ?? "—",
                t.PurchaseOrder?.SupplierName ?? "—",
                t.Warehouse?.Name ?? "—",
                SupervisorName(t.AssignedSupervisorUserId),
                t.Status.ToString(),
                t.GateInTime,
                t.DockInTime,
                t.UnloadingStartTime,
                t.DockOutTime,
                t.GateOutTime,
                Minutes(t.GateInTime, t.GateOutTime),
                Minutes(processingStart, processingEnd),
                t.InspectionLines.Sum(l => l.ReceivedQty),
                t.InspectionLines.Where(l => l.Condition != MaterialCondition.Ok).Sum(l => l.ReceivedQty),
                t.Grn?.GrnNumber);
        }).ToList();

        var outwardRows = outward.Select(t =>
        {
            var processingStart = t.ActualLoadingStartedAt ?? t.LoadingStartTime ?? t.DockInTime;
            var processingEnd = t.DockOutTime ?? t.GateOutTime;
            return new OutwardReportRowDto(
                t.Id,
                t.OutwardTxnNumber,
                t.Vehicle?.Number ?? "—",
                t.DispatchOrder?.DispatchOrderNumber ?? "—",
                t.DispatchOrder?.CustomerName ?? "—",
                t.Warehouse?.Name ?? "—",
                SupervisorName(t.AssignedSupervisorUserId),
                t.Status.ToString(),
                t.CreatedTime,
                t.GateInTime,
                t.DockInTime,
                t.LoadingStartTime,
                t.DockOutTime,
                t.GateOutTime,
                Minutes(t.GateInTime, t.GateOutTime),
                Minutes(processingStart, processingEnd),
                t.LoadLines.Sum(l => l.LoadedQty),
                t.DispatchNote?.IsPartial ?? false,
                t.ExceptionReason?.ToString(),
                t.DispatchNote?.DispatchNoteNumber);
        }).ToList();

        var turnarounds = inwardRows.Select(r => r.TurnaroundMinutes)
            .Concat(outwardRows.Select(r => r.TurnaroundMinutes))
            .Where(m => m is > 0).Select(m => m!.Value).ToList();
        var processings = inwardRows.Select(r => r.ProcessingMinutes)
            .Concat(outwardRows.Select(r => r.ProcessingMinutes))
            .Where(m => m is > 0).Select(m => m!.Value).ToList();

        var summary = new ReportSummaryDto(
            InwardCount: inwardRows.Count,
            OutwardCount: outwardRows.Count,
            CompletedCount: inward.Count(t => t.Status == InwardStatus.Completed) + outward.Count(t => t.Status == OutwardStatus.Completed),
            TotalReceivedQty: inwardRows.Sum(r => r.ReceivedQty),
            TotalLoadedQty: outwardRows.Sum(r => r.LoadedQty),
            TotalExceptionQty: inwardRows.Sum(r => r.ExceptionQty),
            AvgTurnaroundMinutes: turnarounds.Count > 0 ? Math.Round(turnarounds.Average(), 1) : 0,
            AvgProcessingMinutes: processings.Count > 0 ? Math.Round(processings.Average(), 1) : 0);

        return Ok(new DetailedReportDto(fromDate, toDate, summary, inwardRows, outwardRows));
    }

    private static decimal? Minutes(DateTime? start, DateTime? end) =>
        start is { } s && end is { } e && e > s ? Math.Round((decimal)(e - s).TotalMinutes, 1) : null;
}

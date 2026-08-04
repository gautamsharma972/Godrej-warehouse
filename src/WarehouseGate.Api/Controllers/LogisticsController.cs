using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/logistics")]
[Authorize(Roles = "LogisticsManager")]
public class LogisticsController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly AuditService _audit;
    private readonly IHubContext<InwardHub> _hub;

    public LogisticsController(WarehouseGateDbContext db, AuditService audit, IHubContext<InwardHub> hub)
    {
        _db = db;
        _audit = audit;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string CurrentUserName => User.FindFirstValue("displayName") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

    private async Task<int?> GetCallerRegionIdAsync() =>
        await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.RegionId).FirstOrDefaultAsync();

    // A dispatch's From/To warehouse can each legitimately sit in a different region than the
    // uploader's own (e.g. West region warehouse shipping to a North region CFA) - so the pick
    // lists offer the full master, and row-level access is enforced separately by requiring at
    // least one side (From or To) to fall in the caller's own region.
    private async Task<bool> IsRowInCallerScopeAsync(int fromWarehouseId, int toWarehouseId)
    {
        var regionId = await GetCallerRegionIdAsync();
        if (regionId is null)
        {
            return false;
        }

        return await _db.Warehouses.AnyAsync(w =>
            (w.Id == fromWarehouseId || w.Id == toWarehouseId) && w.RegionId == regionId);
    }

    // ============================ WAREHOUSES (full master - From/To can cross regions) ============================

    [HttpGet("warehouses")]
    public async Task<ActionResult<List<WarehouseDto>>> GetWarehouses()
    {
        var full = await _db.Warehouses
            .Include(w => w.Region).Include(w => w.State).Include(w => w.City).Include(w => w.Country)
            .OrderBy(w => w.Name)
            .ToListAsync();

        return Ok(full.Select(w => new WarehouseDto(
            w.Id, w.Name, w.WarehouseType.ToString(),
            w.RegionId, w.Region!.Name, w.StateId, w.State!.Name, w.CityId, w.City!.Name, w.CountryId, w.Country!.Name,
            w.SlaTargetMinutes, w.DockOperatingHoursPerDay, w.ShiftHoursPerDay)).ToList());
    }

    // ============================ VEHICLE LOGISTICS RECORDS ============================

    // Defaults to the last 30 days (by CreatedAtUtc, inclusive calendar days) so the Dispatch Plan
    // list doesn't grow unbounded over the life of a region - Logistics Managers widen or narrow
    // that window with the Created From/To filters, same convention as ReportsController.GetDetailed.
    [HttpGet("vehicle-records")]
    public async Task<ActionResult<List<VehicleLogisticsRecordDto>>> GetVehicleRecords([FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var regionId = await GetCallerRegionIdAsync();
        if (regionId is null)
        {
            return Ok(new List<VehicleLogisticsRecordDto>());
        }

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

        // Capped to the most recent 500 records within the selected window so a region with years
        // of vehicle logistics data can't silently balloon this into an unbounded payload.
        var records = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .Where(r => (r.FromWarehouse!.RegionId == regionId || r.ToWarehouse!.RegionId == regionId)
                && r.CreatedAtUtc >= rangeStart && r.CreatedAtUtc < rangeEnd)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(MaxVehicleRecordResults)
            .ToListAsync();

        var (liveInfo, extraRows) = await ResolveLiveDispatchDataAsync(records);
        var dtos = records.Select(r => ToDto(r, liveInfo.GetValueOrDefault(r.Id))).Concat(extraRows).ToList();
        return Ok(dtos);
    }

    private const int MaxVehicleRecordResults = 500;
    private const int MaxRangeDays = 366;

    [HttpPost("vehicle-records")]
    public async Task<ActionResult<VehicleLogisticsRecordDto>> CreateVehicleRecord(UpsertVehicleLogisticsRecordRequest request)
    {
        var validation = await ValidateWarehousesAsync(request.FromWarehouseId, request.ToWarehouseId);
        if (validation is not null)
        {
            return validation;
        }

        var record = new VehicleLogisticsRecord
        {
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim(),
            PoNumber = request.PoNumber,
            InwardTransactionId = request.InwardTransactionId,
            TransporterName = request.TransporterName,
            DriverName = request.DriverName,
            DriverPhone = request.DriverPhone,
            VehicleType = request.VehicleType,
            Sku = request.Sku.Trim(),
            SkuCode = request.SkuCode,
            BoxQuantity = request.BoxQuantity,
            DepartureDate = request.DepartureDate,
            EtaDateTime = request.EtaDateTime,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            // Always InTransit on creation regardless of what the request sends - manual add and
            // Excel import both start a record's life the same way.
            Status = VehicleLogisticsStatus.InTransit,
            CreatedByUserId = CurrentUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.VehicleLogisticsRecords.Add(record);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", record.Id, AuditAction.Created,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' created.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();

        return await GetVehicleRecordDtoAsync(record.Id);
    }

    [HttpPut("vehicle-records/{id:int}")]
    public async Task<ActionResult<VehicleLogisticsRecordDto>> UpdateVehicleRecord(int id, UpsertVehicleLogisticsRecordRequest request)
    {
        var record = await _db.VehicleLogisticsRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (!await IsRowInCallerScopeAsync(record.FromWarehouseId, record.ToWarehouseId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This record is outside your region." });
        }

        if (record.Status != VehicleLogisticsStatus.InTransit)
        {
            return BadRequest(new { message = "Dispatch records can only be edited while still in-transit." });
        }

        var validation = await ValidateWarehousesAsync(request.FromWarehouseId, request.ToWarehouseId);
        if (validation is not null)
        {
            return validation;
        }

        record.VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim();
        record.PoNumber = request.PoNumber;
        record.InwardTransactionId = request.InwardTransactionId;
        record.TransporterName = request.TransporterName;
        record.DriverName = request.DriverName;
        record.DriverPhone = request.DriverPhone;
        record.VehicleType = request.VehicleType;
        record.Sku = request.Sku.Trim();
        record.SkuCode = request.SkuCode;
        record.BoxQuantity = request.BoxQuantity;
        record.DepartureDate = request.DepartureDate;
        record.EtaDateTime = request.EtaDateTime;
        record.FromWarehouseId = request.FromWarehouseId;
        record.ToWarehouseId = request.ToWarehouseId;
        // Editing lets a Logistics Manager manually override status (e.g. Inactive); leaving it
        // unset in the request keeps whatever the automatic Inward/Outward sync last set.
        if (request.Status is { } status)
        {
            record.Status = status;
        }
        record.UpdatedByUserId = CurrentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", record.Id, AuditAction.Updated,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' updated.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();

        return await GetVehicleRecordDtoAsync(record.Id);
    }

    [HttpDelete("vehicle-records/{id:int}")]
    public async Task<IActionResult> DeleteVehicleRecord(int id)
    {
        var record = await _db.VehicleLogisticsRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (!await IsRowInCallerScopeAsync(record.FromWarehouseId, record.ToWarehouseId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This record is outside your region." });
        }

        if (record.Status != VehicleLogisticsStatus.InTransit)
        {
            return BadRequest(new { message = "Dispatch records can only be deleted while still in-transit." });
        }

        // Soft delete - the record stays queryable/filterable by status (matches the "Deleted"
        // status option in the UI) rather than disappearing from the audit trail entirely.
        record.Status = VehicleLogisticsStatus.Deleted;
        record.UpdatedByUserId = CurrentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", id, AuditAction.Deleted,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' deleted.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();
        return NoContent();
    }

    [HttpPost("vehicle-records/upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<VehicleLogisticsUploadResultDto>> UploadVehicleRecords(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        var allWarehouses = await _db.Warehouses.ToListAsync();
        var regionId = await GetCallerRegionIdAsync();

        List<VehicleLogisticsRecord> parsedRows;
        List<VehicleLogisticsUploadRowErrorDto> errors;
        await using (var stream = file.OpenReadStream())
        {
            (parsedRows, errors) = VehicleLogisticsExcelParser.Parse(stream, CurrentUserId, allWarehouses, regionId);
        }

        var (insertedCount, updatedCount) = await VehicleLogisticsRecordUpsertService.UpsertAsync(_db, parsedRows, CurrentUserId);

        if (insertedCount > 0 || updatedCount > 0)
        {
            await _audit.LogAsync("VehicleLogisticsRecord", 0, AuditAction.Created,
                $"Imported {insertedCount} new / updated {updatedCount} existing vehicle logistics record(s) from '{file.FileName}' ({errors.Count} row(s) skipped).",
                CurrentUserId, CurrentUserName);
            await BroadcastChangedAsync();
        }

        return Ok(new VehicleLogisticsUploadResultDto(insertedCount, updatedCount, errors));
    }

    // Both warehouses must exist, be distinct, and at least one must fall in the caller's own
    // region - shared by Create and Update so the rule can't drift between the two.
    private async Task<ActionResult?> ValidateWarehousesAsync(int fromWarehouseId, int toWarehouseId)
    {
        if (fromWarehouseId == toWarehouseId)
        {
            return BadRequest(new { message = "From and To warehouse cannot be the same." });
        }

        var warehouseIds = await _db.Warehouses
            .Where(w => w.Id == fromWarehouseId || w.Id == toWarehouseId)
            .Select(w => w.Id)
            .ToListAsync();
        if (!warehouseIds.Contains(fromWarehouseId) || !warehouseIds.Contains(toWarehouseId))
        {
            return BadRequest(new { message = "From/To warehouse not found." });
        }

        if (!await IsRowInCallerScopeAsync(fromWarehouseId, toWarehouseId))
        {
            return BadRequest(new { message = "Neither the From nor the To warehouse is in your region." });
        }

        return null;
    }

    // Additive nudge, not load-bearing - other Logistics Managers viewing the same Dispatch Plan
    // list, and Office users viewing their pending inward/outward Dispatch Plan panels, just
    // refetch on receipt, same pattern as OfficeRealtimeClient's job-change events.
    private Task BroadcastChangedAsync() =>
        _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

    private async Task<ActionResult<VehicleLogisticsRecordDto>> GetVehicleRecordDtoAsync(int id)
    {
        var record = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .FirstAsync(r => r.Id == id);
        var (liveInfo, _) = await ResolveLiveDispatchDataAsync(new List<VehicleLogisticsRecord> { record });
        return Ok(ToDto(record, liveInfo.GetValueOrDefault(record.Id)));
    }

    private sealed record LiveDispatchInfo(
        decimal? PickListQty, decimal? LoadedQty, decimal? PhysicalQty,
        string? VehicleNumber, string? DriverName, string? DriverPhone, string? TransporterName);

    // Mirrors InwardService.ResolveDispatchQuantitiesAsync's join pattern, extended with
    // Vehicle/Driver/Transporter - a Dispatch Plan row's own stored fields only ever get patched
    // once the DESTINATION Office links a vehicle (InwardService.TryClaimDispatchPlanForLinkingAsync);
    // the SOURCE Office's own vehicle link (OutwardService.LinkVehicleAsync) only ever lands on the
    // OutwardTransaction itself, so the Logistics Manager would otherwise see "-" for the whole
    // window between the source loading the truck and the destination eventually linking it too.
    //
    // Also synthesizes a read-only extra row (IsExtra = true) for every DispatchOrderLine on those
    // same Outward transactions whose ProductName matches none of the passed-in records' Sku - SKUs
    // the source supervisor added during loading (3D Load Plan Workspace "Add SKU") that have no
    // Dispatch Plan row at all, mirroring InwardService.ResolveDispatchQuantitiesAsync's identical
    // fix for the destination side's "Expected material" list.
    private async Task<(Dictionary<int, LiveDispatchInfo> ByRecordId, List<VehicleLogisticsRecordDto> ExtraRows)> ResolveLiveDispatchDataAsync(
        List<VehicleLogisticsRecord> records)
    {
        var outwardTransactionIds = records
            .Where(r => r.ConsumedByOutwardTransactionId.HasValue)
            .Select(r => r.ConsumedByOutwardTransactionId!.Value)
            .Distinct()
            .ToList();

        var inwardTransactionIds = records
            .Where(r => r.ConsumedByInwardTransactionId.HasValue)
            .Select(r => r.ConsumedByInwardTransactionId!.Value)
            .Distinct()
            .ToList();

        if (outwardTransactionIds.Count == 0 && inwardTransactionIds.Count == 0)
        {
            return (new Dictionary<int, LiveDispatchInfo>(), new List<VehicleLogisticsRecordDto>());
        }

        var transactions = await _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.DispatchOrder!).ThenInclude(d => d.Lines)
            .Where(t => outwardTransactionIds.Contains(t.Id))
            .ToListAsync();

        var loadLines = await _db.OutwardLoadLines
            .Include(l => l.DispatchOrderLine)
            .Where(l => outwardTransactionIds.Contains(l.OutwardTransactionId))
            .ToListAsync();

        var inwardTransactions = await _db.InwardTransactions
            .Include(t => t.PurchaseOrder!).ThenInclude(po => po.Lines)
            .Include(t => t.InspectionLines)
            .Where(t => inwardTransactionIds.Contains(t.Id))
            .ToListAsync();

        // Mirrors InwardJobDetail.razor's own FormatPhysicalQty exactly: Short means it never
        // physically arrived at all, Mismatch means it turned out to be a different SKU entirely -
        // neither counts toward this line's physical receipt. Null (not "-") until at least one
        // inspection line exists for it, distinct from a legitimate 0 (fully short-received).
        decimal? ComputePhysicalQty(int inwardTransactionId, string productName)
        {
            var inwardTransaction = inwardTransactions.FirstOrDefault(t => t.Id == inwardTransactionId);
            var line = inwardTransaction?.PurchaseOrder?.Lines.FirstOrDefault(l => l.ProductName == productName);
            if (line is null)
            {
                return null;
            }

            var inspectionLines = inwardTransaction!.InspectionLines.Where(l => l.PurchaseOrderLineId == line.Id).ToList();
            if (inspectionLines.Count == 0)
            {
                return null;
            }

            return inspectionLines
                .Where(l => l.Condition is MaterialCondition.Ok or MaterialCondition.Damaged or MaterialCondition.Excess)
                .Sum(l => l.ReceivedQty);
        }

        var byRecordId = new Dictionary<int, LiveDispatchInfo>();
        foreach (var record in records)
        {
            var physicalQty = record.ConsumedByInwardTransactionId is { } inwardTransactionId
                ? ComputePhysicalQty(inwardTransactionId, record.Sku)
                : null;

            if (record.ConsumedByOutwardTransactionId is not { } outwardTransactionId)
            {
                if (physicalQty is not null)
                {
                    byRecordId[record.Id] = new LiveDispatchInfo(null, null, physicalQty, null, null, null, null);
                }
                continue;
            }

            var transaction = transactions.FirstOrDefault(t => t.Id == outwardTransactionId);
            if (transaction is null)
            {
                continue;
            }

            var pickListQty = transaction.DispatchOrder?.Lines
                .FirstOrDefault(l => l.ProductName == record.Sku)?.OrderedQty;
            var loadedQty = loadLines
                .FirstOrDefault(l => l.OutwardTransactionId == outwardTransactionId && l.DispatchOrderLine!.ProductName == record.Sku)?.LoadedQty;

            byRecordId[record.Id] = new LiveDispatchInfo(
                pickListQty, loadedQty, physicalQty,
                transaction.Vehicle?.Number, transaction.DriverName, transaction.DriverMobile, transaction.TransporterName);
        }

        var extraRows = new List<VehicleLogisticsRecordDto>();
        foreach (var outwardTransactionId in outwardTransactionIds)
        {
            var transaction = transactions.FirstOrDefault(t => t.Id == outwardTransactionId);
            var recordsForTransaction = records.Where(r => r.ConsumedByOutwardTransactionId == outwardTransactionId).ToList();
            if (transaction?.DispatchOrder is null || recordsForTransaction.Count == 0)
            {
                continue;
            }

            var matchedSkus = recordsForTransaction.Select(r => r.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var context = recordsForTransaction[0];

            foreach (var line in transaction.DispatchOrder.Lines.Where(l => !matchedSkus.Contains(l.ProductName)))
            {
                var loadedQty = loadLines
                    .FirstOrDefault(l => l.OutwardTransactionId == outwardTransactionId && l.DispatchOrderLine!.ProductName == line.ProductName)?.LoadedQty;
                var physicalQty = context.ConsumedByInwardTransactionId is { } inwardTransactionId
                    ? ComputePhysicalQty(inwardTransactionId, line.ProductName)
                    : null;

                extraRows.Add(new VehicleLogisticsRecordDto(
                    -line.Id, transaction.Vehicle?.Number ?? context.VehicleNumber, context.PoNumber, context.InwardTransactionId,
                    transaction.TransporterName ?? context.TransporterName, transaction.DriverName ?? context.DriverName,
                    transaction.DriverMobile ?? context.DriverPhone, context.VehicleType,
                    line.ProductName, null, (int)Math.Round(line.OrderedQty),
                    line.OrderedQty, loadedQty, physicalQty,
                    context.DepartureDate, context.EtaDateTime,
                    context.FromWarehouseId, context.FromWarehouse!.Name, context.ToWarehouseId, context.ToWarehouse!.Name,
                    context.Status.ToString(), context.CreatedAtUtc, IsExtra: true));
            }
        }

        return (byRecordId, extraRows);
    }

    private static VehicleLogisticsRecordDto ToDto(Domain.VehicleLogisticsRecord r, LiveDispatchInfo? live = null) => new(
        r.Id, r.VehicleNumber ?? live?.VehicleNumber, r.PoNumber, r.InwardTransactionId,
        r.TransporterName ?? live?.TransporterName, r.DriverName ?? live?.DriverName, r.DriverPhone ?? live?.DriverPhone,
        r.VehicleType, r.Sku, r.SkuCode, r.BoxQuantity,
        live?.PickListQty, live?.LoadedQty, live?.PhysicalQty,
        r.DepartureDate, r.EtaDateTime,
        r.FromWarehouseId, r.FromWarehouse!.Name, r.ToWarehouseId, r.ToWarehouse!.Name,
        r.Status.ToString(), r.CreatedAtUtc);
}

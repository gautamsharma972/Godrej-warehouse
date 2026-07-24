using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly DashboardAnalyticsService _analytics;
    private readonly WarehouseScopeResolver _scopeResolver;

    public DashboardController(WarehouseGateDbContext db, DashboardAnalyticsService analytics, WarehouseScopeResolver scopeResolver)
    {
        _db = db;
        _analytics = analytics;
        _scopeResolver = scopeResolver;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
    {
        var warehouseIdScope = await _scopeResolver.ResolveAsync(User);
        var warehouseCount = warehouseIdScope?.Count ?? await _db.Warehouses.CountAsync();
        var scopeLabel = await ResolveScopeLabelAsync();

        var today = DateTime.UtcNow.Date;

        var inwardQuery = _db.InwardTransactions.AsQueryable();
        var outwardQuery = _db.OutwardTransactions.AsQueryable();
        if (warehouseIdScope is not null)
        {
            inwardQuery = inwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
            outwardQuery = outwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
        }

        var summary = new DashboardSummaryDto(
            WarehouseCount: warehouseCount,
            TodayInwardGateIns: await inwardQuery.CountAsync(t => t.GateInTime >= today),
            TodayOutwardGateIns: await outwardQuery.CountAsync(t => t.GateInTime != null && t.GateInTime >= today),
            ActiveInwardJobs: await inwardQuery.CountAsync(t => t.Status != InwardStatus.Completed),
            ActiveOutwardJobs: await outwardQuery.CountAsync(t => t.Status != OutwardStatus.Completed),
            CompletedTodayInward: await inwardQuery.CountAsync(t => t.Status == InwardStatus.Completed && t.DockOutTime >= today),
            CompletedTodayOutward: await outwardQuery.CountAsync(t => t.Status == OutwardStatus.Completed && t.DockOutTime >= today),
            ScopeLabel: scopeLabel);

        return Ok(summary);
    }

    // LogisticsManager -> their own region's name; Office -> their own warehouse's name;
    // SuperAdmin/others -> null (unscoped, nothing single to label).
    private async Task<string?> ResolveScopeLabelAsync()
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        return role switch
        {
            nameof(UserRole.LogisticsManager) => await _db.Users
                .Where(u => u.Id == CurrentUserId)
                .Select(u => u.Region!.Name)
                .FirstOrDefaultAsync(),
            nameof(UserRole.Office) => await _db.Users
                .Where(u => u.Id == CurrentUserId)
                .Select(u => u.Warehouse!.Name)
                .FirstOrDefaultAsync(),
            _ => null
        };
    }

    // Supervisor leaderboard, daily/weekly/monthly trend charts, and the advanced KPI tiles -
    // see DashboardAnalyticsService for how each is computed. Same warehouse scoping as summary.
    [HttpGet("analytics")]
    public async Task<ActionResult<DashboardAnalyticsDto>> GetAnalytics()
    {
        var warehouseIdScope = await _scopeResolver.ResolveAsync(User);
        return Ok(await _analytics.BuildAsync(warehouseIdScope));
    }
}

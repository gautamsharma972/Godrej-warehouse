using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Filters on FromWarehouseId/ToWarehouseId directly against the SAME List<int>? warehouse scope
// WarehouseScopeResolver computes for every other plugin (null = unscoped for SuperAdmin, "every
// warehouse in my region" for LogisticsManager) - this is exactly equivalent to the original
// "either side's warehouse.RegionId == caller's region" check, since that scope list IS "every
// warehouse in the caller's region" by construction, but it means LogisticsPlugin no longer needs
// its own separate region-lookup query, and now also works for SuperAdmin for free.
public class LogisticsPlugin
{
    private const int MaxListItems = 50;

    private readonly WarehouseGateDbContext _db;
    private readonly List<int>? _warehouseScope;

    public LogisticsPlugin(WarehouseGateDbContext db, List<int>? warehouseScope)
    {
        _db = db;
        _warehouseScope = warehouseScope;
    }

    // Display-only rows (no ActionType/ActionValue) - unlike the Office-side lists, there's no
    // "edit an in-transit Dispatch Plan entry" action yet for Logistics Manager to jump into.
    public AssistantListResultDto? LastListResult { get; private set; }

    [KernelFunction("get_in_transit_vehicles")]
    [Description(
        "Gets vehicles still in transit (dispatch plan entries not yet claimed for outward pick-up or " +
        "inward receipt) visible to the caller - their own region for Logistics Manager, every warehouse " +
        "for SuperAdmin - grouped by vehicle. When the user asks to narrow this down to one vehicle, " +
        "call this AGAIN with vehicleNumber filled in - never just repeat the unfiltered list or try to " +
        "filter it yourself in your reply.")]
    public async Task<string> GetInTransitVehiclesAsync(
        [Description("Optional - only show this vehicle number (a partial match is fine). Leave blank for every vehicle.")] string? vehicleNumber = null)
    {
        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return "The caller has no region assigned, so no dispatch plan records can be listed.";
        }

        var query = _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse)
            .Include(r => r.ToWarehouse)
            .Where(r => r.Status == VehicleLogisticsStatus.InTransit);

        if (_warehouseScope is not null)
        {
            query = query.Where(r => _warehouseScope.Contains(r.FromWarehouseId) || _warehouseScope.Contains(r.ToWarehouseId));
        }

        var filterSuffix = "";
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(r => r.VehicleNumber.Contains(vehicleNumber));
            filterSuffix = $" (vehicle contains '{vehicleNumber}')";
        }

        var records = await query.OrderByDescending(r => r.CreatedAtUtc).Take(200).ToListAsync();
        if (records.Count == 0)
        {
            // The guidance sentence is bracketed as "[Assistant note...]" rather than appended as
            // plain trailing text - see OutwardJobsPlugin's identical zero-count branch for why
            // (a real observed failure: the model echoing this whole return string, guidance
            // included, verbatim as its reply instead of composing its own sentence).
            return $"Zero vehicles are in transit{filterSuffix}. [Assistant note: tell the user plainly " +
                   "that there are none right now - never say \"here are the vehicles\" or anything " +
                   "implying results exist when this count is zero. Never repeat this bracketed note " +
                   "itself in your reply.]";
        }

        var byVehicle = records.GroupBy(r => r.VehicleNumber).ToList();
        var items = byVehicle.Take(MaxListItems).Select(g =>
        {
            var first = g.First();
            var eta = first.EtaDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "ETA not set";
            var canOpenDispatchPlan = _warehouseScope is not null;
            return new AssistantListItemDto(
                g.Key,
                $"{first.FromWarehouse!.Name} -> {first.ToWarehouse!.Name}, {g.Count()} SKU line(s)",
                eta,
                NavigationUrl: canOpenDispatchPlan ? "/logistics/vehicle-records" : null,
                NavigationLabel: canOpenDispatchPlan ? "Open Dispatch Plan" : null);
        }).ToList();
        LastListResult = new AssistantListResultDto($"{byVehicle.Count} vehicle(s) in transit{filterSuffix}", items, byVehicle.Count);

        // See the zero-count branch above for why the guidance clause is bracketed.
        return $"{byVehicle.Count} vehicle(s) in transit found{filterSuffix} - already shown to the " +
               "user as a list. [Assistant note: just acknowledge briefly, don't restate the details, " +
               "and never repeat this bracketed note itself in your reply.]";
    }
}

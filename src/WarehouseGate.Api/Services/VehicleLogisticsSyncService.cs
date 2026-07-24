using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// Keeps a Logistics Manager's own Dispatch Plan rows in sync with the real Inward/Outward
// Supervisor workflow: once a job for a given vehicle actually finishes, any of that vehicle's
// Dispatch Plan records where the finished job's warehouse is either the From or the To side
// flip to Completed. Matched by vehicle number + warehouse only - Dispatch Plan rows aren't
// otherwise linked to a specific transaction, they're the Logistics Manager's own advance plan
// that may or may not correspond to a real job.
public class VehicleLogisticsSyncService
{
    private readonly WarehouseGateDbContext _db;

    public VehicleLogisticsSyncService(WarehouseGateDbContext db)
    {
        _db = db;
    }

    // By the time a job actually completes, its Dispatch Plan rows are essentially always
    // already InProgress (claimed at pick-list-generation/gate-check-in time, long before
    // completion) rather than still InTransit - both are accepted here so completion doesn't
    // silently no-op and leave the row stuck at InProgress forever.
    public Task MarkCompletedAsync(string? vehicleNumber, int? warehouseId) =>
        UpdateStatusAsync(vehicleNumber, warehouseId, VehicleLogisticsStatus.Completed,
            VehicleLogisticsStatus.InTransit, VehicleLogisticsStatus.InProgress);

    // Undoes MarkCompletedAsync when a Supervisor restarts a job that was already Completed.
    // Reverts to InProgress, not InTransit - the row is still claimed by (linked to) this same
    // job via ConsumedByOutwardTransactionId/ConsumedByInwardTransactionId, so InTransit would
    // wrongly make it look unclaimed and available for a fresh pick-list/check-in claim.
    public Task MarkInProgressAsync(string? vehicleNumber, int? warehouseId) =>
        UpdateStatusAsync(vehicleNumber, warehouseId, VehicleLogisticsStatus.InProgress, VehicleLogisticsStatus.Completed);

    private async Task UpdateStatusAsync(string? vehicleNumber, int? warehouseId, VehicleLogisticsStatus to, params VehicleLogisticsStatus[] from)
    {
        if (string.IsNullOrWhiteSpace(vehicleNumber) || warehouseId is null)
        {
            return;
        }

        var records = await _db.VehicleLogisticsRecords
            .Where(r => r.VehicleNumber == vehicleNumber &&
                (r.FromWarehouseId == warehouseId || r.ToWarehouseId == warehouseId) &&
                from.Contains(r.Status))
            .ToListAsync();

        if (records.Count == 0)
        {
            return;
        }

        foreach (var record in records)
        {
            record.Status = to;
            record.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}

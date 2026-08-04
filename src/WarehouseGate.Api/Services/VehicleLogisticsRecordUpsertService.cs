using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// Shared by both Dispatch Plan Excel import paths - LogisticsController's own upload endpoint and
// the Assistant chat's DispatchPlanExcelImportPlugin - so a re-upload of a corrected/updated file
// behaves the same way regardless of which path was used.
public static class VehicleLogisticsRecordUpsertService
{
    // Re-uploading a corrected/updated Dispatch Plan file shouldn't pile up duplicate rows for the
    // same shipment line - a row is treated as a duplicate of an existing one (and updated in
    // place) when PO Number, From/To Warehouse, and the SKU identifier (SKU Code preferred when
    // both sides have one, else the SKU name) all match. Only InTransit records are eligible to be
    // matched/updated - once a row has been tagged/claimed by a real job (or manually deactivated/
    // deleted), it's a different lifecycle stage and a re-upload creates a fresh row instead of
    // silently mutating data an in-flight job may already depend on.
    public static async Task<(int Inserted, int Updated)> UpsertAsync(
        WarehouseGateDbContext db, List<VehicleLogisticsRecord> parsedRows, string userId)
    {
        if (parsedRows.Count == 0)
        {
            return (0, 0);
        }

        var poNumbers = parsedRows.Select(r => r.PoNumber).Distinct().ToList();
        var candidates = await db.VehicleLogisticsRecords
            .Where(r => poNumbers.Contains(r.PoNumber) && r.Status == VehicleLogisticsStatus.InTransit)
            .ToListAsync();

        var inserted = 0;
        var updated = 0;

        foreach (var row in parsedRows)
        {
            var existing = candidates.FirstOrDefault(c =>
                c.PoNumber == row.PoNumber &&
                c.FromWarehouseId == row.FromWarehouseId &&
                c.ToWarehouseId == row.ToWarehouseId &&
                SkuIdentifierMatches(c, row.Sku, row.SkuCode));

            if (existing is not null)
            {
                existing.Sku = row.Sku;
                existing.SkuCode = row.SkuCode ?? existing.SkuCode;
                existing.BoxQuantity = row.BoxQuantity;
                existing.DepartureDate = row.DepartureDate;
                existing.EtaDateTime = row.EtaDateTime;
                existing.VehicleNumber = row.VehicleNumber ?? existing.VehicleNumber;
                existing.TransporterName = row.TransporterName ?? existing.TransporterName;
                existing.DriverName = row.DriverName ?? existing.DriverName;
                existing.DriverPhone = row.DriverPhone ?? existing.DriverPhone;
                existing.VehicleType = row.VehicleType ?? existing.VehicleType;
                existing.InwardTransactionId = row.InwardTransactionId ?? existing.InwardTransactionId;
                existing.UpdatedByUserId = userId;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                db.VehicleLogisticsRecords.Add(row);
                // So a later row in this same file, sharing the same key, updates this one in place
                // instead of being inserted as a second duplicate.
                candidates.Add(row);
                inserted++;
            }
        }

        await db.SaveChangesAsync();
        return (inserted, updated);
    }

    private static bool SkuIdentifierMatches(VehicleLogisticsRecord existing, string sku, string? skuCode)
    {
        if (!string.IsNullOrWhiteSpace(skuCode) && !string.IsNullOrWhiteSpace(existing.SkuCode))
        {
            return string.Equals(existing.SkuCode, skuCode, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(existing.Sku, sku, StringComparison.OrdinalIgnoreCase);
    }
}

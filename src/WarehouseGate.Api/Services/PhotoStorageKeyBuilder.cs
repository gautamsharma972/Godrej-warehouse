using Microsoft.EntityFrameworkCore;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// Single source of truth for the folder layout every captured photo/document lands under,
// regardless of which IPhotoStorageService implementation is active (local disk nests it as real
// directories, S3 uses it as the key prefix) - InwardService/OutwardService/OutwardLoadPlanService
// all call this right before SaveAsync instead of building the string themselves, so the
// convention only ever needs to change in one place.
public static class PhotoStorageKeyBuilder
{
    // "{orgCode}/{warehouseName}/{transactionId}" or, when a human-readable transaction number
    // exists, "{orgCode}/{warehouseName}/{transactionId}/{transactionNumber}" - e.g.
    // "GCPL/Mumbai DC/42/TXN-20260806073506678". transactionNumber is null for an outward gate
    // arrival captured before it's ever linked to a real OutwardTransaction (see
    // OutwardGateArrival's own class header comment) - there's genuinely no number yet at that
    // point, so the path just stops at the id instead of inventing one.
    public static async Task<string> BuildAsync(
        WarehouseGateDbContext db, int transactionId, string? transactionNumber,
        int organizationId, int? warehouseId, CancellationToken ct = default)
    {
        var orgCode = await db.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.Code)
            .FirstOrDefaultAsync(ct);
        var warehouseName = warehouseId.HasValue
            ? await db.Warehouses.Where(w => w.Id == warehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync(ct)
            : null;

        // "/" would otherwise be read as an extra path segment; a missing org/warehouse (shouldn't
        // happen given the FK, but this runs before that constraint would ever be hit) still needs
        // somewhere sane to land rather than throwing mid-upload.
        string Clean(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Replace('/', '-').Trim();

        var segments = new List<string>
        {
            Clean(orgCode, "unknown-org"),
            Clean(warehouseName, "unassigned-warehouse"),
            transactionId.ToString()
        };
        if (!string.IsNullOrWhiteSpace(transactionNumber))
        {
            segments.Add(Clean(transactionNumber, transactionId.ToString()));
        }

        return string.Join('/', segments);
    }
}

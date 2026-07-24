using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// The one shared answer to "which warehouses may this caller see?" - used by every read-only
// aggregation endpoint (dashboard summary/analytics, reports). SuperAdmin sees everything
// (null = unscoped), LogisticsManager sees every warehouse in their own Region, everyone else
// (Office/Security/Supervisor) sees only their own Warehouse. Existing pre-migration
// transactions with a null WarehouseId are invisible to scoped roles (only SuperAdmin sees
// them) - an accepted gap until enough warehouse-attributed history accumulates.
public class WarehouseScopeResolver
{
    private readonly WarehouseGateDbContext _db;

    public WarehouseScopeResolver(WarehouseGateDbContext db)
    {
        _db = db;
    }

    public async Task<List<int>?> ResolveAsync(ClaimsPrincipal user)
    {
        var role = user.FindFirstValue(ClaimTypes.Role);
        if (role == nameof(UserRole.SuperAdmin))
        {
            return null;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var appUser = await _db.Users.FirstAsync(u => u.Id == userId);

        if (role == nameof(UserRole.LogisticsManager))
        {
            return await _db.Warehouses
                .Where(w => w.RegionId == appUser.RegionId)
                .Select(w => w.Id)
                .ToListAsync();
        }

        return appUser.WarehouseId is null ? new List<int>() : new List<int> { appUser.WarehouseId.Value };
    }
}

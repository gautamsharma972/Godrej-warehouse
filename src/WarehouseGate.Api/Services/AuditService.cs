using Microsoft.EntityFrameworkCore;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

public class AuditService
{
    private readonly WarehouseGateDbContext _db;

    public AuditService(WarehouseGateDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string entityName, int entityId, AuditAction action, string summary, string userId, string userName)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            ChangedByUserId = userId,
            ChangedByName = userName,
            ChangedAtUtc = DateTime.UtcNow,
            Summary = summary
        });
        await _db.SaveChangesAsync();
    }

    // Overload for the service layer (Inward/OutwardService), which only carries the caller's
    // user id - controllers keep passing their own CurrentUserName via the overload above, this
    // one resolves the display name itself so mobile-driven mutations can be audited too.
    public async Task LogAsync(string entityName, int entityId, AuditAction action, string summary, string userId)
    {
        var userName = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync();

        await LogAsync(entityName, entityId, action, summary, userId,
            string.IsNullOrWhiteSpace(userName) ? "Unknown" : userName);
    }
}

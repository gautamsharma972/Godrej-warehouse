namespace WarehouseGate.Domain;

// Marker for entities that always belong to exactly one Organization. WarehouseGateDbContext
// uses this to apply a global query filter and to auto-stamp OrganizationId on insert, so
// individual controllers/services never need to filter or set it themselves.
public interface ITenantScoped
{
    int OrganizationId { get; set; }
}

// Same idea, but for the handful of entities where "no organization" is itself a valid,
// meaningful state (a PlatformAdmin ApplicationUser, or an AuditLog row for a platform-level
// action) rather than an unset value.
public interface IOptionallyTenantScoped
{
    int? OrganizationId { get; set; }
}

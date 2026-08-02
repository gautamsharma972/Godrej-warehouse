namespace WarehouseGate.Infrastructure;

// Resolves which Organization the current DbContext operation is scoped to. The real
// implementation (in the Api project) reads the "orgId" claim off the authenticated HTTP
// request. WarehouseGateDbContext treats HasContext == false as "scoping disabled" - the state
// that's always true outside a web request (EF migrations, SeedData at startup, unit tests),
// where there is no caller to scope to and every row must remain visible.
public interface ICurrentTenantProvider
{
    bool HasContext { get; }
    int? OrganizationId { get; }
}

public sealed class NullCurrentTenantProvider : ICurrentTenantProvider
{
    public bool HasContext => false;
    public int? OrganizationId => null;
}

namespace WarehouseGate.Domain;

// The tenant root. Every org-scoped entity (see ITenantScoped) carries an OrganizationId that
// must point at a row here. Not itself tenant-scoped - managed exclusively by the platform site.
public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Short, unique, entered at login (alongside username/password) to disambiguate which
    // org's account a username belongs to, since usernames are only unique per-organization.
    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // 0 = unlimited. Enforced by AdminController when creating users/warehouses.
    public int MaxUsers { get; set; }
    public int MaxWarehouses { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

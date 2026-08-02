using Microsoft.AspNetCore.Identity;
using WarehouseGate.Domain;

namespace WarehouseGate.Infrastructure;

public class ApplicationUser : IdentityUser, IOptionallyTenantScoped
{
    public UserRole Role { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    // Null only for UserRole.PlatformAdmin, who manages Organizations rather than belonging to one.
    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? RegionId { get; set; }
    public Region? Region { get; set; }
}

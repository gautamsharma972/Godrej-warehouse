using Microsoft.AspNetCore.Identity;
using WarehouseGate.Domain;

namespace WarehouseGate.Infrastructure;

public class ApplicationUser : IdentityUser
{
    public UserRole Role { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public int? RegionId { get; set; }
    public Region? Region { get; set; }
}

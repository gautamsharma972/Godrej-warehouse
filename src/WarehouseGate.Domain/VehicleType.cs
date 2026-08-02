namespace WarehouseGate.Domain;

public class VehicleType : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int OrganizationId { get; set; }
}

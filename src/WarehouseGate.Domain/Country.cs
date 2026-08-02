namespace WarehouseGate.Domain;

public class Country : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int OrganizationId { get; set; }
}

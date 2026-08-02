namespace WarehouseGate.Domain;

public class State : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public int OrganizationId { get; set; }
}

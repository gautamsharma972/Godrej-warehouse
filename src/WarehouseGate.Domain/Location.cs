namespace WarehouseGate.Domain;

public class Location : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int RegionId { get; set; }
    public Region? Region { get; set; }

    public int StateId { get; set; }
    public State? State { get; set; }

    public int CityId { get; set; }
    public City? City { get; set; }

    public int OrganizationId { get; set; }
}

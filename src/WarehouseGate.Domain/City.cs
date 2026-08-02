namespace WarehouseGate.Domain;

public class City : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int StateId { get; set; }
    public State? State { get; set; }

    public int OrganizationId { get; set; }
}

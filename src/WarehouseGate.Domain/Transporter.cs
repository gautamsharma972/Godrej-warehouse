namespace WarehouseGate.Domain;

public class Transporter : ITenantScoped
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; } = true;

    public int OrganizationId { get; set; }
}

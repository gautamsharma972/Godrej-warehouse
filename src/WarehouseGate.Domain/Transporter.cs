namespace WarehouseGate.Domain;

public class Transporter
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public bool IsActive { get; set; } = true;
}

namespace WarehouseGate.Domain;

public class AuditLog
{
    public int Id { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public AuditAction Action { get; set; }
    public string ChangedByUserId { get; set; } = string.Empty;
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string Summary { get; set; } = string.Empty;
}

namespace WarehouseGate.Domain;

public class OutwardDispatchNote
{
    public int Id { get; set; }

    public int OutwardTransactionId { get; set; }
    public OutwardTransaction? OutwardTransaction { get; set; }

    public string DispatchNoteNumber { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool IsPartial { get; set; }
}

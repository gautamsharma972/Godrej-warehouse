namespace WarehouseGate.Domain;

public class GoodsReceiptNote
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public string GrnNumber { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool HasExceptions { get; set; }
}

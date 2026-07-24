namespace WarehouseGate.Domain;

public class PhotoEvidence
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public PhotoType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
}

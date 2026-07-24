namespace WarehouseGate.Domain;

public class InwardDocument
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public DocumentType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

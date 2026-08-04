namespace WarehouseGate.Domain;

public class PhotoEvidence
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public PhotoType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }

    // Set only for Type == SkuCondition (per-SKU Receiving Inspection photos, max 2 per line - see
    // InwardService.AddPhotoAsync); null for all other (job-level) photo types.
    public int? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }
}

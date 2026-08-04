namespace WarehouseGate.Domain;

public class OutwardPhotoEvidence
{
    public int Id { get; set; }

    public int OutwardTransactionId { get; set; }
    public OutwardTransaction? OutwardTransaction { get; set; }

    // Set only for Type=LoadGroupConfirmation photos captured against a specific
    // load-plan group during Actual Loading Confirmation; null for the existing
    // whole-transaction evidence types (Vehicle/Material/Exception/Seal etc).
    public int? OutwardLoadPlanGroupId { get; set; }
    public OutwardLoadPlanGroup? OutwardLoadPlanGroup { get; set; }

    // Set only for Type=SkuLoaded (per-SKU Load Confirmation photos, max 2 per line - see
    // OutwardService.AddPhotoAsync); null for all other (job-level) photo types. Mirrors
    // PhotoEvidence.PurchaseOrderLineId on the Inward side.
    public int? DispatchOrderLineId { get; set; }
    public DispatchOrderLine? DispatchOrderLine { get; set; }

    public OutwardPhotoType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
}

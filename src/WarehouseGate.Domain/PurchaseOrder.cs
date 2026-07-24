namespace WarehouseGate.Domain;

public class PurchaseOrder
{
    public int Id { get; set; }
    public string PONumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? ExpectedDeliveryDate { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = new();
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal ExpectedQty { get; set; }
    public string UnitOfMeasure { get; set; } = "PCS";

    // Only populated for lines synthesized from a Dispatch Plan match (see
    // InwardService.TryClaimDispatchPlanForInwardAsync) - null for a manually-entered PO, or for
    // a Dispatch Plan line whose Outward side hasn't reached that stage yet. PickListQty is the
    // quantity Office set (or left as the planned box quantity) when generating the pick list at
    // the source warehouse; LoadedQty is what the source warehouse's supervisor actually loaded.
    public decimal? PickListQty { get; set; }
    public decimal? LoadedQty { get; set; }
}

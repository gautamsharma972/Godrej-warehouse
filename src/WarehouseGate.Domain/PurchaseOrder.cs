namespace WarehouseGate.Domain;

public class PurchaseOrder : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
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

    // True for a line synthesized from a DispatchOrderLine that had no matching Dispatch Plan row
    // at all - a SKU the source warehouse's supervisor added during loading (3D Load Plan Workspace
    // "Add SKU"), beyond what Logistics/Office originally planned. See
    // InwardService.ResolveDispatchQuantitiesAsync's ExtraDispatchLine. False for every ordinary
    // Dispatch-Plan-matched or manually-entered PO line.
    public bool IsExtra { get; set; }

    // Which DispatchOrderLine (source warehouse) this line was synthesized from, if any - plain
    // informational id, no navigation/FK (same lightweight style as PickListQty/LoadedQty above).
    // Lets InwardService.SyncExtraLinesAsync tell "already mirrored" apart from "not yet mirrored"
    // by id rather than by ProductName, which breaks the moment two DispatchOrderLines (e.g. two
    // separate "Add SKU" additions of the same product) share a name.
    public int? SourceDispatchOrderLineId { get; set; }
}

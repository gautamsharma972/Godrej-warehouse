namespace WarehouseGate.Domain;

public class InspectionLine
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public int PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    public decimal ReceivedQty { get; set; }
    public MaterialCondition Condition { get; set; }
    public string? Notes { get; set; }
}

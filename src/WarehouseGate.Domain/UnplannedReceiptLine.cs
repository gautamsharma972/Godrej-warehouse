namespace WarehouseGate.Domain;

// The real SKU actually received in place of an expected one on an Inward job - backs the
// Supervisor-facing "Mismatch SKU Details" section. Distinct from MaterialCondition.Mismatch, which
// just records how much of an *expected* PO line's quantity turned out not to be that SKU at all;
// this row names what it actually was. Supervisor records these against the SKU Master (see
// Product) alongside the normal per-PO-line InspectionLine breakdown.
public class UnplannedReceiptLine
{
    public int Id { get; set; }

    public int InwardTransactionId { get; set; }
    public InwardTransaction? InwardTransaction { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

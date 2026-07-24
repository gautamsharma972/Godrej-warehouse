namespace WarehouseGate.Domain;

public class OutwardLoadLine
{
    public int Id { get; set; }

    public int OutwardTransactionId { get; set; }
    public OutwardTransaction? OutwardTransaction { get; set; }

    public int DispatchOrderLineId { get; set; }
    public DispatchOrderLine? DispatchOrderLine { get; set; }

    public decimal LoadedQty { get; set; }
    public int LoadSequence { get; set; }
    public string? Notes { get; set; }
}

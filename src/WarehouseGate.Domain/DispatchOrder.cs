namespace WarehouseGate.Domain;

public class DispatchOrder : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string DispatchOrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime RequestedDate { get; set; }

    public List<DispatchOrderLine> Lines { get; set; } = new();
}

public class DispatchOrderLine
{
    public int Id { get; set; }
    public int DispatchOrderId { get; set; }
    public DispatchOrder? DispatchOrder { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public string UnitOfMeasure { get; set; } = "PCS";

    // Optional link to the Product master for weight/dimension data (used by the suggested
    // loading-sequence algorithm). Nullable since lines aren't required to be linked - ProductName
    // above is still the source of truth for display.
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    // Where this line's cartons need to end up on delivery - lets a supervisor
    // search the Delivery Unloading View by stop, not just by product name.
    public string DeliveryLocation { get; set; } = string.Empty;
}

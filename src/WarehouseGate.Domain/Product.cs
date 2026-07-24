namespace WarehouseGate.Domain;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public decimal LengthCm { get; set; }
    public decimal WidthCm { get; set; }
    public decimal HeightCm { get; set; }

    // SKU master data, managed by SuperAdmin (Admin > Products). Defaults keep
    // existing/un-configured products behaving exactly as before: stackable,
    // effectively unlimited stack height, medium weight class, no fixed color
    // (falls back to the order-index palette assignment).
    public string SkuCode { get; set; } = string.Empty;
    public WeightCategory Category { get; set; } = WeightCategory.Medium;
    public bool IsStackable { get; set; } = true;
    public int MaxStackLayers { get; set; } = 99;
    public string? ColorHex { get; set; }
}

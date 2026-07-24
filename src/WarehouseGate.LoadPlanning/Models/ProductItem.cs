namespace WarehouseGate.LoadPlanning.Models;

public sealed record ProductItem
{
    public required string Sku { get; init; }
    public required string Description { get; init; }
    public required int Quantity { get; init; }

    public required double Length { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double Weight { get; init; }

    public FragilityLevel FragilityLevel { get; init; } = FragilityLevel.None;
    public Stackability Stackability { get; init; } = Stackability.StackableAny;
    public double MaxStackWeight { get; init; } = double.MaxValue;
    public double MaxStackHeight { get; init; } = double.MaxValue;

    // SKU-master-driven stacking limits for LoadPlanValidator's advisory checks
    // (distinct from Stackability/MaxStackHeight above, which feed the
    // automatic optimizer's placement search). IsStackable=false means this SKU
    // can't have another carton of itself placed directly above it at all;
    // MaxStackLayers caps how many of its own layers deep a group may go.
    public bool IsStackable { get; init; } = true;
    public int MaxStackLayers { get; init; } = int.MaxValue;
    public double CrushStrength { get; init; } = double.MaxValue;
    public OrientationRestriction OrientationRestriction { get; init; } = OrientationRestriction.None;
    public HazardClass HazardClass { get; init; } = HazardClass.None;
    public TemperatureClass TemperatureClass { get; init; } = TemperatureClass.Ambient;
    public int DeliverySequence { get; init; }
    public int LoadingPriority { get; init; } = 100;
    public PackagingType PackagingType { get; init; } = PackagingType.Carton;
    public bool RotationAllowed { get; init; } = true;

    public double Volume => Length * Width * Height;
    public double Density => Volume <= 0 ? 0 : Weight / Volume;
}

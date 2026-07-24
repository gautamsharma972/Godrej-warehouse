namespace WarehouseGate.LoadPlanning.Models;

public sealed class VehicleProfile
{
    public required string Name { get; init; }

    public required double Length { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }

    public required double MaxPayload { get; init; }
    public double FrontAxleLimit { get; init; } = double.MaxValue;
    public double RearAxleLimit { get; init; } = double.MaxValue;
    public double EmptyWeight { get; init; }

    public DoorPosition DoorPosition { get; init; } = DoorPosition.Rear;
    public LoadingType LoadingType { get; init; } = LoadingType.Manual;
    public bool SideLoadingSupport { get; init; }
    public bool DoubleDeckSupport { get; init; }

    // Refrigeration capability - general dry-freight trucks support neither by default.
    // A truck that supports Frozen is assumed cold enough to also carry Chilled goods.
    public bool SupportsChilled { get; init; }
    public bool SupportsFrozen { get; init; }

    public double Volume => Length * Width * Height;
}

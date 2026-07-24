namespace WarehouseGate.LoadPlanning.Models;

// Vehicle-local axes: X = width (left-right), Y = height (floor-up), Z = length/depth
// (Z=0 is the back wall - the first thing loaded, matching the existing mobile viewer's
// "1 = loaded first / placed at back" convention).
public readonly record struct Vector3D(double X, double Y, double Z);

public sealed record Placement
{
    public required Vector3D Position { get; init; }
    public required Orientation Orientation { get; init; }
    public required double DimX { get; init; }
    public required double DimY { get; init; }
    public required double DimZ { get; init; }

    public double Volume => DimX * DimY * DimZ;

    public bool Overlaps(Placement other)
    {
        return Position.X < other.Position.X + other.DimX && other.Position.X < Position.X + DimX
            && Position.Y < other.Position.Y + other.DimY && other.Position.Y < Position.Y + DimY
            && Position.Z < other.Position.Z + other.DimZ && other.Position.Z < Position.Z + DimZ;
    }
}

public sealed record FreeCuboid(double X, double Y, double Z, double DimX, double DimY, double DimZ)
{
    public double Volume => DimX * DimY * DimZ;
}

public sealed record PlacedItem
{
    public required ProductItem Product { get; init; }
    public required Placement Placement { get; init; }
    public required int StackLevel { get; init; }
    public required double SupportArea { get; init; }
    public required int LoadSequence { get; init; }
    public required string Color { get; init; }

    // Soft-rule reasons (e.g. "Support: 62% supported", "DoorClearance: stacked
    // near the door...") that applied to this item's winning candidate during
    // placement search. Empty when the placement was clean.
    public IReadOnlyList<string> ViolationReasons { get; init; } = Array.Empty<string>();
}

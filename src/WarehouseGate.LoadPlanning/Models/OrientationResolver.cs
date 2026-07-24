namespace WarehouseGate.LoadPlanning.Models;

// Maps one of the 6 axis-aligned box rotations onto vehicle-local (X, Y, Z) extents.
// Paired orientations share which physical dimension ends up vertical (Y) - e.g.
// LWH/WLH both keep Height vertical ("upright"), just swapping which of
// Length/Width faces the door.
public static class OrientationResolver
{
    public static (double DimX, double DimY, double DimZ) Resolve(Orientation orientation, double length, double width, double height) =>
        orientation switch
        {
            Orientation.LWH => (length, height, width),
            Orientation.WLH => (width, height, length),
            Orientation.LHW => (length, width, height),
            Orientation.HLW => (height, width, length),
            Orientation.WHL => (width, length, height),
            Orientation.HWL => (height, length, width),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation))
        };

    public static IReadOnlyList<Orientation> AllowedOrientations(ProductItem product)
    {
        if (!product.RotationAllowed)
        {
            return new[] { Orientation.LWH };
        }

        if (product.OrientationRestriction == OrientationRestriction.UprightOnly)
        {
            return new[] { Orientation.LWH, Orientation.WLH };
        }

        return Enum.GetValues<Orientation>();
    }
}

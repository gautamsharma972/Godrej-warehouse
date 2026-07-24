using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Optimization;

// Generates candidate placements for an item: every allowed orientation, tried
// against every currently-free cuboid it could fit inside, anchored at that
// cuboid's back-bottom-left corner (the standard "corner point" heuristic for
// guillotine-style bin packing).
public static class CandidateGenerator
{
    public static IEnumerable<(Placement Placement, FreeCuboid Source)> GenerateCandidates(
        ProductItem product, VehicleProfile vehicle, IReadOnlyList<FreeCuboid> freeSpace)
    {
        foreach (var orientation in OrientationResolver.AllowedOrientations(product))
        {
            var (dimX, dimY, dimZ) = OrientationResolver.Resolve(orientation, product.Length, product.Width, product.Height);
            if (dimX > vehicle.Width || dimY > vehicle.Height || dimZ > vehicle.Length)
            {
                continue;
            }

            foreach (var cuboid in freeSpace)
            {
                if (dimX <= cuboid.DimX && dimY <= cuboid.DimY && dimZ <= cuboid.DimZ)
                {
                    yield return (new Placement
                    {
                        Position = new Vector3D(cuboid.X, cuboid.Y, cuboid.Z),
                        Orientation = orientation,
                        DimX = dimX,
                        DimY = dimY,
                        DimZ = dimZ
                    }, cuboid);
                }
            }
        }
    }
}

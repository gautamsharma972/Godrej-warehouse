using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Optimization;

// Guillotine-style free-cuboid tracking, extracted out of LoadOptimizer so the
// zone-constrained placer (ZoneGroupPlacer) can seed and split free space
// scoped to a single zone's sub-cuboid using the exact same logic the
// whole-vehicle auto-optimizer already relies on.
internal static class FreeSpaceOps
{
    public const double Epsilon = 0.5;

    // Every existing free cuboid that the new placement intersects is clipped down
    // to the (up to 6) sub-regions still free of it. This can leave a little
    // overlap between the resulting free cuboids (unlike a strict guillotine
    // partition), which only costs a bit of candidate-generation efficiency,
    // never correctness.
    public static void SplitFreeSpace(List<FreeCuboid> freeSpace, Placement placed)
    {
        var next = new List<FreeCuboid>();
        foreach (var free in freeSpace)
        {
            if (!Intersects(free, placed))
            {
                next.Add(free);
                continue;
            }

            if (free.X < placed.Position.X)
            {
                next.Add(free with { DimX = placed.Position.X - free.X });
            }
            if (placed.Position.X + placed.DimX < free.X + free.DimX)
            {
                next.Add(free with { X = placed.Position.X + placed.DimX, DimX = free.X + free.DimX - (placed.Position.X + placed.DimX) });
            }
            if (free.Y < placed.Position.Y)
            {
                next.Add(free with { DimY = placed.Position.Y - free.Y });
            }
            if (placed.Position.Y + placed.DimY < free.Y + free.DimY)
            {
                next.Add(free with { Y = placed.Position.Y + placed.DimY, DimY = free.Y + free.DimY - (placed.Position.Y + placed.DimY) });
            }
            if (free.Z < placed.Position.Z)
            {
                next.Add(free with { DimZ = placed.Position.Z - free.Z });
            }
            if (placed.Position.Z + placed.DimZ < free.Z + free.DimZ)
            {
                next.Add(free with { Z = placed.Position.Z + placed.DimZ, DimZ = free.Z + free.DimZ - (placed.Position.Z + placed.DimZ) });
            }
        }

        var meaningful = next.Where(f => f.DimX > Epsilon && f.DimY > Epsilon && f.DimZ > Epsilon).ToList();
        freeSpace.Clear();
        foreach (var candidate in meaningful)
        {
            if (!meaningful.Any(other => !ReferenceEquals(other, candidate) && Contains(other, candidate)))
            {
                freeSpace.Add(candidate);
            }
        }
    }

    public static bool Intersects(FreeCuboid f, Placement p) =>
        f.X < p.Position.X + p.DimX && p.Position.X < f.X + f.DimX &&
        f.Y < p.Position.Y + p.DimY && p.Position.Y < f.Y + f.DimY &&
        f.Z < p.Position.Z + p.DimZ && p.Position.Z < f.Z + f.DimZ;

    public static bool Contains(FreeCuboid outer, FreeCuboid inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y && outer.Z <= inner.Z &&
        outer.X + outer.DimX >= inner.X + inner.DimX &&
        outer.Y + outer.DimY >= inner.Y + inner.DimY &&
        outer.Z + outer.DimZ >= inner.Z + inner.DimZ;
}

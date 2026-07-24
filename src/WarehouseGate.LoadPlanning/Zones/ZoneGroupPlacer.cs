using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Optimization;

namespace WarehouseGate.LoadPlanning.Zones;

public sealed record ZonePlacementRequest(
    ProductItem UnitProduct,
    int Quantity,
    FreeCuboid ZoneBounds,
    Orientation Orientation,
    int? Rows,
    int? Columns,
    int? Layers,
    IReadOnlyList<PlacedItem> AlreadyPlacedInVehicle);

public sealed record ZonePlacementResult(
    IReadOnlyList<Placement> UnitPlacements,
    int PlacedCount,
    int OverflowCount,
    int ResolvedRows,
    int ResolvedColumns,
    int ResolvedLayers,
    Placement GroupBoundingBox,
    IReadOnlyList<string> Warnings);

// Turns "SKU + qty + zone + orientation + stacking (auto or rows x columns x
// layers)" into concrete unit placements, per the spec's explicit "avoid
// complex 3D dragging - tap a zone, then pick placement options" interaction.
// Deliberately not the same code path as LoadOptimizer's whole-vehicle
// automatic bin-packing: this is a supervisor-directed, deterministic grid
// fill scoped to one zone's sub-cuboid, reusing the same free-space
// fragmentation logic (FreeSpaceOps) so a zone that already holds another
// group's cartons is respected rather than overlapped.
public static class ZoneGroupPlacer
{
    public static ZonePlacementResult Place(ZonePlacementRequest request)
    {
        var (dimX, dimY, dimZ) = OrientationResolver.Resolve(
            request.Orientation, request.UnitProduct.Length, request.UnitProduct.Width, request.UnitProduct.Height);

        var warnings = new List<string>();
        var qty = Math.Max(0, request.Quantity);

        if (dimX > request.ZoneBounds.DimX || dimY > request.ZoneBounds.DimY || dimZ > request.ZoneBounds.DimZ)
        {
            warnings.Add("This item doesn't fit in the selected zone at this orientation - try a different orientation or zone.");
            return new ZonePlacementResult(
                Array.Empty<Placement>(), 0, qty, 0, 0, 0,
                EmptyBoundingBox(request.ZoneBounds), warnings);
        }

        // Carve out whatever space in this zone is already occupied by other
        // groups (in this or a different zone that happens to overlap it),
        // exactly like the whole-vehicle optimizer does for the full vehicle.
        var freeSpace = new List<FreeCuboid> { request.ZoneBounds };
        foreach (var placedItem in request.AlreadyPlacedInVehicle)
        {
            if (FreeSpaceOps.Intersects(request.ZoneBounds, placedItem.Placement))
            {
                FreeSpaceOps.SplitFreeSpace(freeSpace, placedItem.Placement);
            }
        }

        if (freeSpace.Count == 0)
        {
            warnings.Add("The selected zone is already fully occupied by other placed items.");
            return new ZonePlacementResult(
                Array.Empty<Placement>(), 0, qty, 0, 0, 0,
                EmptyBoundingBox(request.ZoneBounds), warnings);
        }

        var orderedFreeSpace = freeSpace.OrderByDescending(f => f.Volume).ToList();
        var placements = new List<Placement>();
        var remaining = qty;
        var isAuto = request.Rows is null || request.Columns is null || request.Layers is null;
        int resolvedRows = 0, resolvedColumns = 0, resolvedLayers = 0;

        if (isAuto)
        {
            foreach (var cuboid in orderedFreeSpace)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var columns = (int)Math.Floor(cuboid.DimX / dimX);
                var layers = (int)Math.Floor(cuboid.DimY / dimY);
                var rows = (int)Math.Floor(cuboid.DimZ / dimZ);
                if (columns <= 0 || layers <= 0 || rows <= 0)
                {
                    continue;
                }

                if (placements.Count == 0)
                {
                    (resolvedRows, resolvedColumns, resolvedLayers) = (rows, columns, layers);
                }

                remaining -= FillGrid(placements, cuboid, dimX, dimY, dimZ, rows, columns, layers, remaining, request.Orientation);
            }
        }
        else
        {
            // Manual stacking anchors a single grid block in the largest free
            // region - spilling one manual grid across disjoint fragments of a
            // partially-occupied zone isn't a coherent "rows x columns x
            // layers" for the supervisor to reason about. If the zone is
            // fragmented, place what fits in the largest region and report the
            // rest as overflow (the supervisor can place the remainder as a
            // second group).
            var cuboid = orderedFreeSpace[0];
            var maxColumns = (int)Math.Floor(cuboid.DimX / dimX);
            var maxLayers = (int)Math.Floor(cuboid.DimY / dimY);
            var maxRows = (int)Math.Floor(cuboid.DimZ / dimZ);

            resolvedColumns = Math.Clamp(request.Columns!.Value, 0, maxColumns);
            resolvedLayers = Math.Clamp(request.Layers!.Value, 0, maxLayers);
            resolvedRows = Math.Clamp(request.Rows!.Value, 0, maxRows);

            if (resolvedColumns < request.Columns || resolvedLayers < request.Layers || resolvedRows < request.Rows)
            {
                warnings.Add(
                    $"Requested stacking ({request.Rows}x{request.Columns}x{request.Layers}) was reduced to " +
                    $"{resolvedRows}x{resolvedColumns}x{resolvedLayers} (rows x columns x layers) to fit the zone.");
            }

            remaining -= FillGrid(placements, cuboid, dimX, dimY, dimZ, resolvedRows, resolvedColumns, resolvedLayers, remaining, request.Orientation);
        }

        var placedCount = placements.Count;
        var overflowCount = qty - placedCount;
        if (overflowCount > 0 && placedCount > 0)
        {
            warnings.Add($"Only {placedCount} of {qty} cartons fit in the selected zone at this orientation/stacking.");
        }
        else if (overflowCount > 0 && placedCount == 0 && warnings.Count == 0)
        {
            warnings.Add("None of the requested quantity fits in the selected zone at this orientation/stacking.");
        }

        return new ZonePlacementResult(
            placements, placedCount, overflowCount, resolvedRows, resolvedColumns, resolvedLayers,
            ComputeBoundingBox(placements, request.ZoneBounds, request.Orientation), warnings);
    }

    // Fills up to `remaining` units into the given cuboid in a row(Z)/layer(Y)/
    // column(X) grid, bottom layer first (stability, matches the whole-vehicle
    // optimizer's low-position preference). Returns how many units were placed.
    private static int FillGrid(
        List<Placement> placements, FreeCuboid cuboid, double dimX, double dimY, double dimZ,
        int rows, int columns, int layers, int remaining, Orientation orientation)
    {
        var placedHere = 0;
        for (var l = 0; l < layers && placedHere < remaining; l++)
        {
            for (var r = 0; r < rows && placedHere < remaining; r++)
            {
                for (var c = 0; c < columns && placedHere < remaining; c++)
                {
                    placements.Add(new Placement
                    {
                        Position = new Vector3D(cuboid.X + c * dimX, cuboid.Y + l * dimY, cuboid.Z + r * dimZ),
                        Orientation = orientation,
                        DimX = dimX,
                        DimY = dimY,
                        DimZ = dimZ
                    });
                    placedHere++;
                }
            }
        }

        return placedHere;
    }

    private static Placement ComputeBoundingBox(IReadOnlyList<Placement> placements, FreeCuboid zoneBounds, Orientation orientation)
    {
        if (placements.Count == 0)
        {
            return EmptyBoundingBox(zoneBounds);
        }

        var minX = placements.Min(p => p.Position.X);
        var minY = placements.Min(p => p.Position.Y);
        var minZ = placements.Min(p => p.Position.Z);
        var maxX = placements.Max(p => p.Position.X + p.DimX);
        var maxY = placements.Max(p => p.Position.Y + p.DimY);
        var maxZ = placements.Max(p => p.Position.Z + p.DimZ);

        return new Placement
        {
            Position = new Vector3D(minX, minY, minZ),
            Orientation = orientation,
            DimX = maxX - minX,
            DimY = maxY - minY,
            DimZ = maxZ - minZ
        };
    }

    private static Placement EmptyBoundingBox(FreeCuboid zoneBounds) => new()
    {
        Position = new Vector3D(zoneBounds.X, zoneBounds.Y, zoneBounds.Z),
        Orientation = Orientation.LWH,
        DimX = 0,
        DimY = 0,
        DimZ = 0
    };
}

using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Zones;

public sealed record FreePlacementRequest(
    ProductItem UnitProduct,
    int Quantity,
    Vector3D Anchor,
    Orientation Orientation,
    int? Rows,
    int? Columns,
    int? Layers,
    VehicleProfile Vehicle,
    IReadOnlyList<PlacedItem> AlreadyPlacedInVehicle);

public sealed record FreePlacementResult(
    IReadOnlyList<Placement> UnitPlacements,
    int PlacedCount,
    int OverflowCount,
    int ResolvedRows,
    int ResolvedColumns,
    int ResolvedLayers,
    Placement GroupBoundingBox,
    IReadOnlyList<string> Warnings,
    bool IsValid);

// Free-coordinate counterpart to ZoneGroupPlacer: the supervisor picks the
// anchor point directly (tap/drag in the 3D view) instead of a zone cell, so
// this only has to resolve the grid fill and check validity at that exact
// spot - no free-space search across the whole vehicle. "Auto" placement fills
// one practical layer at the chosen height: across the remaining width first,
// then down the remaining length. Supervisors can still place on top by choosing
// a roof/height anchor, or request multiple layers through manual rows/columns/layers.
public static class FreeGroupPlacer
{
    private const double Epsilon = 0.5;

    public static FreePlacementResult Place(FreePlacementRequest request)
    {
        var (dimX, dimY, dimZ) = OrientationResolver.Resolve(
            request.Orientation, request.UnitProduct.Length, request.UnitProduct.Width, request.UnitProduct.Height);

        var warnings = new List<string>();
        var qty = Math.Max(0, request.Quantity);

        var isAuto = request.Rows is null || request.Columns is null || request.Layers is null;
        var anchor = request.Anchor;
        int rows, columns, layers;
        if (isAuto)
        {
            (anchor, columns, rows, layers) = ResolveAutoGrid(request, qty, dimX, dimY, dimZ);
        }
        else
        {
            rows = Math.Max(0, request.Rows!.Value);
            columns = Math.Max(0, request.Columns!.Value);
            layers = Math.Max(0, request.Layers!.Value);
        }

        var groupDimX = columns * dimX;
        var groupDimY = layers * dimY;
        var groupDimZ = rows * dimZ;
        var boundingBox = new Placement
        {
            Position = anchor,
            Orientation = request.Orientation,
            DimX = groupDimX,
            DimY = groupDimY,
            DimZ = groupDimZ
        };

        var outOfBounds =
            anchor.X < -Epsilon || anchor.Y < -Epsilon || anchor.Z < -Epsilon ||
            anchor.X + groupDimX > request.Vehicle.Width + Epsilon ||
            anchor.Y + groupDimY > request.Vehicle.Height + Epsilon ||
            anchor.Z + groupDimZ > request.Vehicle.Length + Epsilon;

        if (outOfBounds)
        {
            warnings.Add(
                $"Cannot place: this group ({groupDimX:0} x {groupDimZ:0} x {groupDimY:0} cm, L x W x H) " +
                "extends beyond the vehicle at this position.");
            return new FreePlacementResult(
                Array.Empty<Placement>(), 0, qty, rows, columns, layers, boundingBox, warnings, IsValid: false);
        }

        PlacedItem? collidingWith = null;
        foreach (var placedItem in request.AlreadyPlacedInVehicle)
        {
            if (placedItem.Placement.Overlaps(boundingBox))
            {
                collidingWith = placedItem;
                break;
            }
        }

        if (collidingWith is not null)
        {
            warnings.Add($"Cannot place: overlaps {collidingWith.Product.Description}.");
            return new FreePlacementResult(
                Array.Empty<Placement>(), 0, qty, rows, columns, layers, boundingBox, warnings, IsValid: false);
        }

        var placements = new List<Placement>();
        var placed = 0;
        for (var l = 0; l < layers && placed < qty; l++)
        {
            for (var r = 0; r < rows && placed < qty; r++)
            {
                for (var c = 0; c < columns && placed < qty; c++)
                {
                    placements.Add(new Placement
                    {
                        Position = new Vector3D(
                            anchor.X + c * dimX, anchor.Y + l * dimY, anchor.Z + r * dimZ),
                        Orientation = request.Orientation,
                        DimX = dimX,
                        DimY = dimY,
                        DimZ = dimZ
                    });
                    placed++;
                }
            }
        }

        var overflow = qty - placed;
        if (overflow > 0)
        {
            warnings.Add($"Only {placed} of {qty} cartons fit in this rows x columns x layers grid.");
        }

        return new FreePlacementResult(
            placements, placed, overflow, rows, columns, layers, boundingBox, warnings, IsValid: true);
    }

    // Resolves the auto grid the way a loading crew fills space: as wide as the free strip
    // allows, stacked up to the SKU's limit, extending down the length - and when the strip
    // is blocked by already-placed cargo, the footprint SHRINKS to fit beside it instead of
    // failing; if the anchor point itself is on top of existing cargo, the whole grid LIFTS
    // onto that stack's roof (real-life "put the rest on top") and re-resolves from there.
    private static (Vector3D Anchor, int Columns, int Rows, int Layers) ResolveAutoGrid(
        FreePlacementRequest request, int qty, double dimX, double dimY, double dimZ)
    {
        var anchor = request.Anchor;
        int columns = 1, rows = 1, layers = 1;

        // Horizontal extent the grid may occupy. Starts as the whole vehicle; a lift onto an
        // existing stack narrows it to that stack's own footprint, so cartons placed on a roof
        // never overhang into thin air beyond their support.
        var limitMinX = 0.0;
        var limitX = request.Vehicle.Width;
        var limitZ = request.Vehicle.Length;

        // Clamp the usable extent to a supporting group's whole roof at the given height -
        // shared by the two ways a grid ends up elevated: the caller handing us an anchor
        // already on a roof, and our own lift below.
        void ClampToRoof(PlacedItem support, double roofTop)
        {
            // Seed from the supporting carton itself - seeding from the anchor cell would
            // inflate the roof past its real edge and let grids overhang it.
            var roofMinX = support.Placement.Position.X;
            var roofMaxX = support.Placement.Position.X + support.Placement.DimX;
            var roofMaxZ = support.Placement.Position.Z + support.Placement.DimZ;
            foreach (var placedItem in request.AlreadyPlacedInVehicle)
            {
                var p = placedItem.Placement;
                if (placedItem.LoadSequence == support.LoadSequence &&
                    Math.Abs(p.Position.Y + p.DimY - roofTop) <= Epsilon)
                {
                    roofMinX = Math.Min(roofMinX, p.Position.X);
                    roofMaxX = Math.Max(roofMaxX, p.Position.X + p.DimX);
                    roofMaxZ = Math.Max(roofMaxZ, p.Position.Z + p.DimZ);
                }
            }

            limitMinX = Math.Max(0, roofMinX);
            limitX = Math.Min(request.Vehicle.Width, roofMaxX);
            limitZ = Math.Min(request.Vehicle.Length, roofMaxZ);
        }

        // The caller may hand us an anchor that's already sitting on a stack (drop/tap on a
        // roof) - find that support and confine the grid to its roof, exactly as if we had
        // lifted onto it ourselves. Otherwise rows would run past the roof edge into thin air.
        if (anchor.Y > 0.5)
        {
            PlacedItem? preSupport = null;
            var bestTop = 0.0;
            foreach (var placedItem in request.AlreadyPlacedInVehicle)
            {
                var p = placedItem.Placement;
                var top = p.Position.Y + p.DimY;
                if (Math.Abs(top - anchor.Y) <= 10 + Epsilon && top > bestTop &&
                    anchor.X < p.Position.X + p.DimX + Epsilon && p.Position.X - Epsilon < anchor.X + dimX &&
                    anchor.Z < p.Position.Z + p.DimZ + Epsilon && p.Position.Z - Epsilon < anchor.Z + dimZ)
                {
                    preSupport = placedItem;
                    bestTop = top;
                }
            }

            if (preSupport is not null)
            {
                ClampToRoof(preSupport, bestTop);
            }
        }

        for (var lift = 0; lift < 12; lift++)
        {
            // Width-first re-anchoring: a real crew builds a cabin-facing wall across the
            // full width, so shift the anchor toward the left wall until as many columns as
            // the quantity wants can fit - otherwise a drop near the right wall degenerates
            // into a one-column rail running down the whole vehicle length.
            var availableWidth = limitX - limitMinX;
            var idealColumns = Math.Max(1, Math.Min((int)Math.Floor((availableWidth + Epsilon) / dimX), Math.Max(1, qty)));
            var neededWidth = idealColumns * dimX;
            if (anchor.X + neededWidth > limitX + Epsilon)
            {
                anchor = anchor with { X = Math.Max(limitMinX, limitX - neededWidth) };
            }
            if (anchor.X < limitMinX)
            {
                anchor = anchor with { X = limitMinX };
            }

            // Same for depth: pull the anchor forward until at least one unit fits before
            // the limit (the roof's rear edge when lifted, the back wall on the floor).
            if (anchor.Z + dimZ > limitZ + Epsilon)
            {
                anchor = anchor with { Z = Math.Max(0, limitZ - dimZ) };
            }

            // If even a single unit still can't fit inside the current extent (e.g. a roof
            // narrower or shallower than one carton), this anchor holds nothing - report
            // zero placed rather than forcing a 1-unit grid that overhangs into thin air.
            if (anchor.X + dimX > limitX + Epsilon || anchor.Z + dimZ > limitZ + Epsilon)
            {
                return (anchor, 0, 0, 0);
            }

            var maxColumns = Math.Max(1, (int)Math.Floor((limitX - anchor.X + Epsilon) / dimX));
            var maxRows = Math.Max(1, (int)Math.Floor((limitZ - anchor.Z + Epsilon) / dimZ));
            var maxLayers = Math.Max(1, (int)Math.Floor((request.Vehicle.Height - anchor.Y + Epsilon) / dimY));
            if (!request.UnitProduct.IsStackable)
            {
                maxLayers = 1;
            }
            else if (request.UnitProduct.MaxStackLayers < maxLayers)
            {
                maxLayers = Math.Max(1, request.UnitProduct.MaxStackLayers);
            }

            columns = Math.Min(maxColumns, Math.Max(1, qty));
            rows = Math.Min(maxRows, Math.Max(1, (int)Math.Ceiling(qty / (double)columns)));
            layers = 1;

            // Shrink the footprint around whatever it collides with. Each pass strictly
            // reduces columns or rows, so this terminates.
            var blockedAtAnchor = false;
            for (var guard = 0; guard < 200; guard++)
            {
                var box = new Placement
                {
                    Position = anchor,
                    Orientation = request.Orientation,
                    DimX = columns * dimX,
                    DimY = layers * dimY,
                    DimZ = rows * dimZ
                };

                PlacedItem? collider = null;
                foreach (var placedItem in request.AlreadyPlacedInVehicle)
                {
                    if (placedItem.Placement.Overlaps(box))
                    {
                        collider = placedItem;
                        break;
                    }
                }

                if (collider is null)
                {
                    // Elevated grids must stand on real cartons, not on the roof's bounding
                    // box: a partial top layer leaves an L-shaped roof, and a rectangle over
                    // its notch would hang in the air. Trim to the largest sub-grid whose
                    // every cell center has a carton top directly beneath it.
                    if (anchor.Y > Epsilon)
                    {
                        (columns, rows) = TrimToSupported(request, anchor, columns, rows, dimX, dimZ);
                        if (columns == 0 || rows == 0)
                        {
                            return (anchor, 0, 0, 0);
                        }
                    }

                    return (anchor, columns, rows, layers);
                }

                var colsBefore = (int)Math.Floor((collider.Placement.Position.X - anchor.X + Epsilon) / dimX);
                var rowsBefore = (int)Math.Floor((collider.Placement.Position.Z - anchor.Z + Epsilon) / dimZ);
                var keepIfShrinkX = colsBefore >= 1 ? colsBefore * rows : -1;
                var keepIfShrinkZ = rowsBefore >= 1 ? columns * rowsBefore : -1;

                if (keepIfShrinkX < 0 && keepIfShrinkZ < 0)
                {
                    blockedAtAnchor = true;
                    break;
                }

                if (keepIfShrinkX >= keepIfShrinkZ)
                {
                    columns = colsBefore;
                }
                else
                {
                    rows = rowsBefore;
                }

                // Keep auto placement to the selected layer; any remainder becomes overflow
                // so the supervisor can choose whether to place it on top or elsewhere.
                layers = Math.Min(layers, maxLayers);
            }

            if (!blockedAtAnchor)
            {
                break;
            }

            // The anchor sits under existing cargo - climb onto the tallest stack covering it.
            var liftTo = anchor.Y;
            PlacedItem? support = null;
            foreach (var placedItem in request.AlreadyPlacedInVehicle)
            {
                var p = placedItem.Placement;
                if (anchor.X < p.Position.X + p.DimX && p.Position.X < anchor.X + dimX &&
                    anchor.Z < p.Position.Z + p.DimZ && p.Position.Z < anchor.Z + dimZ &&
                    p.Position.Y + p.DimY > liftTo)
                {
                    liftTo = p.Position.Y + p.DimY;
                    support = placedItem;
                }
            }

            if (support is null || liftTo <= anchor.Y + Epsilon)
            {
                break; // nothing to climb onto - let the caller report the collision
            }

            anchor = anchor with { Y = liftTo };
            ClampToRoof(support, liftTo);
        }

        return (anchor, columns, rows, layers);
    }

    // Largest leading sub-grid (from the anchor corner) in which every cell center sits on
    // a carton top at exactly the anchor height - i.e. every carton of the lifted grid is
    // at least half-supported by real cargo underneath.
    private static (int Columns, int Rows) TrimToSupported(
        FreePlacementRequest request, Vector3D anchor, int columns, int rows, double dimX, double dimZ)
    {
        int SupportedColsForRow(int r)
        {
            var zc = anchor.Z + r * dimZ + dimZ / 2;
            for (var c = 0; c < columns; c++)
            {
                var xc = anchor.X + c * dimX + dimX / 2;
                var supported = false;
                foreach (var item in request.AlreadyPlacedInVehicle)
                {
                    var p = item.Placement;
                    if (Math.Abs(p.Position.Y + p.DimY - anchor.Y) <= Epsilon &&
                        xc >= p.Position.X && xc < p.Position.X + p.DimX &&
                        zc >= p.Position.Z && zc < p.Position.Z + p.DimZ)
                    {
                        supported = true;
                        break;
                    }
                }

                if (!supported)
                {
                    return c;
                }
            }
            return columns;
        }

        var bestColumns = 0;
        var bestRows = 0;
        var minCols = int.MaxValue;
        for (var r = 0; r < rows; r++)
        {
            minCols = Math.Min(minCols, SupportedColsForRow(r));
            if (minCols == 0)
            {
                break;
            }
            if (minCols * (r + 1) > bestColumns * bestRows)
            {
                bestColumns = minCols;
                bestRows = r + 1;
            }
        }

        return (bestColumns, bestRows);
    }
}

using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Rules;
using WarehouseGate.LoadPlanning.RuleEngine;

namespace WarehouseGate.LoadPlanning.Optimization;

// The hybrid optimizer: First-Fit-Decreasing ordering, Best-Fit candidate
// selection (scored, rule-filtered), guillotine-style free-space tracking with
// rotation support, and a bounded swap-optimization local-search pass.
public sealed class LoadOptimizer
{
    private static readonly string[] Palette =
        { "#4f7cff", "#ff9f43", "#26c281", "#e0568c", "#8e6cff", "#ffcf44", "#3fbfbf", "#ff6b6b" };

    private const double Epsilon = 0.5;
    private const int MaxUnitsPerLine = 50;
    private const int MaxTotalUnits = 3000;
    private const int MaxLocalSearchIterations = 200;

    private readonly IRuleEngine _ruleEngine;
    private readonly ScoringFunction _scoringFunction;

    public LoadOptimizer(IRuleEngine ruleEngine, ScoringFunction scoringFunction)
    {
        _ruleEngine = ruleEngine;
        _scoringFunction = scoringFunction;
    }

    public LoadOptimizationResult Optimize(IReadOnlyList<ProductItem> catalog, VehicleProfile vehicle)
    {
        // Assign colors by first-seen order in the catalog rather than hashing the SKU -
        // a hash can collide between two SKUs (e.g. two lines landing on the same palette
        // slot by chance), which looked like a bug where two different products appeared
        // in the same color. First-seen order guarantees distinct colors as long as there
        // are no more distinct SKUs than palette entries.
        var colorIndexBySku = new Dictionary<string, int>();
        foreach (var item in catalog)
        {
            colorIndexBySku.TryAdd(item.Sku, colorIndexBySku.Count);
        }

        var unplaced = new List<string>();
        var validItems = new List<ProductItem>();

        foreach (var product in catalog)
        {
            if (Fits(product, vehicle))
            {
                validItems.Add(product);
            }
            else
            {
                unplaced.Add(product.Sku);
            }
        }

        var units = ExpandAndOrder(validItems);
        var placed = new List<PlacedItem>();
        var freeSpace = new List<FreeCuboid> { new(0, 0, 0, vehicle.Width, vehicle.Height, vehicle.Length) };
        var sequence = 1;
        var violationCount = 0;

        foreach (var product in units)
        {
            var best = FindBestPlacement(product, vehicle, freeSpace, placed);
            if (best is null)
            {
                unplaced.Add(product.Sku);
                continue;
            }

            var (placement, _, violationReasons) = best.Value;
            violationCount += violationReasons.Count;

            var placedItem = new PlacedItem
            {
                Product = product,
                Placement = placement,
                StackLevel = ComputeStackLevel(placement, placed),
                SupportArea = SupportRule.ComputeSupportRatio(placement, placed),
                LoadSequence = sequence++,
                Color = ColorFor(product.Sku, colorIndexBySku),
                ViolationReasons = violationReasons
            };

            placed.Add(placedItem);
            FreeSpaceOps.SplitFreeSpace(freeSpace, placement);
        }

        RunLocalSearch(placed, vehicle);

        return new LoadOptimizationResult(placed, unplaced, violationCount);
    }

    private static bool Fits(ProductItem product, VehicleProfile vehicle)
    {
        if (product.Length <= 0 || product.Width <= 0 || product.Height <= 0 || product.Weight <= 0)
        {
            return false;
        }

        foreach (var orientation in OrientationResolver.AllowedOrientations(product))
        {
            var (dimX, dimY, dimZ) = OrientationResolver.Resolve(orientation, product.Length, product.Width, product.Height);
            if (dimX <= vehicle.Width && dimY <= vehicle.Height && dimZ <= vehicle.Length)
            {
                return true;
            }
        }

        return false;
    }

    // A hard per-unit cap would silently starve later lines once one line's own
    // quantity fills the whole budget (500 LED bulbs alone would previously have
    // pushed a same-order Extension Cord line out entirely, with no sign it had
    // been dropped). Instead, any line whose quantity exceeds MaxUnitsPerLine is
    // grouped into taller stacked "bundle" units (bundle height = unit height x
    // units-in-bundle) so every line always gets fair, bounded representation -
    // the full requested quantity is still accounted for, just represented as
    // fewer, taller boxes rather than one box per physical unit.
    private static List<ProductItem> ExpandAndOrder(IReadOnlyList<ProductItem> items)
    {
        var units = new List<ProductItem>();
        foreach (var item in items)
        {
            var qty = Math.Max(1, item.Quantity);
            var groupSize = (int)Math.Ceiling(qty / (double)MaxUnitsPerLine);
            var bundleCount = (int)Math.Ceiling(qty / (double)groupSize);

            for (var i = 0; i < bundleCount && units.Count < MaxTotalUnits; i++)
            {
                var unitsInBundle = Math.Min(groupSize, qty - i * groupSize);
                if (unitsInBundle <= 0)
                {
                    break;
                }

                units.Add(unitsInBundle == 1 ? item : item with
                {
                    Height = item.Height * unitsInBundle,
                    Weight = item.Weight * unitsInBundle
                });
            }
        }

        return units
            .OrderBy(i => i.LoadingPriority)
            .ThenByDescending(i => i.Weight)
            .ThenByDescending(i => i.Volume)
            .ToList();
    }

    private (Placement Placement, FreeCuboid Source, IReadOnlyList<string> ViolationReasons)? FindBestPlacement(
        ProductItem product, VehicleProfile vehicle, List<FreeCuboid> freeSpace, List<PlacedItem> placed)
    {
        (Placement Placement, FreeCuboid Source, IReadOnlyList<string> ViolationReasons)? best = null;
        var bestScore = double.NegativeInfinity;

        foreach (var candidate in CandidateGenerator.GenerateCandidates(product, vehicle, freeSpace))
        {
            var context = new RuleEvaluationContext
            {
                Candidate = product,
                CandidatePlacement = candidate.Placement,
                AlreadyPlaced = placed,
                Vehicle = vehicle
            };

            var result = _ruleEngine.Evaluate(context);
            if (!result.Passed)
            {
                continue;
            }

            var score = _scoringFunction.Score(candidate.Placement, candidate.Source, result.TotalPenalty, vehicle);
            if (score > bestScore)
            {
                bestScore = score;
                best = (candidate.Placement, candidate.Source, result.ViolationReasons);
            }
        }

        return best;
    }

    private static int ComputeStackLevel(Placement placement, IReadOnlyList<PlacedItem> alreadyPlaced)
    {
        if (placement.Position.Y <= Epsilon)
        {
            return 0;
        }

        var maxBelowLevel = -1;
        foreach (var below in alreadyPlaced)
        {
            var belowTop = below.Placement.Position.Y + below.Placement.DimY;
            if (Math.Abs(belowTop - placement.Position.Y) > Epsilon || !OverlapsFootprint(placement, below.Placement))
            {
                continue;
            }

            maxBelowLevel = Math.Max(maxBelowLevel, below.StackLevel);
        }

        return maxBelowLevel + 1;
    }

    private static bool OverlapsFootprint(Placement a, Placement b) =>
        a.Position.X < b.Position.X + b.DimX && b.Position.X < a.Position.X + a.DimX &&
        a.Position.Z < b.Position.Z + b.DimZ && b.Position.Z < a.Position.Z + a.DimZ;

    // Bounded swap-optimization pass: two adjacently-sequenced items with
    // identical footprints can trade spatial positions if doing so improves
    // their combined stability (lower resting position + better support) - safe
    // because swapping two same-sized boxes' positions can't introduce a
    // collision or change anything else's validity.
    private static void RunLocalSearch(List<PlacedItem> placed, VehicleProfile vehicle)
    {
        var iterations = Math.Min(placed.Count, MaxLocalSearchIterations);
        for (var pass = 0; pass < iterations; pass++)
        {
            var improved = false;
            for (var i = 0; i < placed.Count - 1; i++)
            {
                var a = placed[i];
                var b = placed[i + 1];
                if (!SameFootprint(a.Placement, b.Placement))
                {
                    continue;
                }

                var before = StabilityScore(a, vehicle) + StabilityScore(b, vehicle);

                var others = new List<PlacedItem>(placed.Count - 2);
                for (var k = 0; k < placed.Count; k++)
                {
                    if (k != i && k != i + 1)
                    {
                        others.Add(placed[k]);
                    }
                }

                var swappedA = a with
                {
                    Placement = b.Placement,
                    SupportArea = SupportRule.ComputeSupportRatio(b.Placement, others),
                    StackLevel = ComputeStackLevel(b.Placement, others)
                };
                var swappedB = b with
                {
                    Placement = a.Placement,
                    SupportArea = SupportRule.ComputeSupportRatio(a.Placement, others),
                    StackLevel = ComputeStackLevel(a.Placement, others)
                };

                var after = StabilityScore(swappedA, vehicle) + StabilityScore(swappedB, vehicle);
                if (after > before)
                {
                    placed[i] = swappedA;
                    placed[i + 1] = swappedB;
                    improved = true;
                }
            }

            if (!improved)
            {
                break;
            }
        }
    }

    private static bool SameFootprint(Placement a, Placement b) =>
        Math.Abs(a.DimX - b.DimX) < Epsilon && Math.Abs(a.DimY - b.DimY) < Epsilon && Math.Abs(a.DimZ - b.DimZ) < Epsilon;

    private static double StabilityScore(PlacedItem item, VehicleProfile vehicle) =>
        (vehicle.Height <= 0 ? 0 : 1 - item.Placement.Position.Y / vehicle.Height) + item.SupportArea;

    private static string ColorFor(string sku, Dictionary<string, int> colorIndexBySku) =>
        Palette[colorIndexBySku.TryGetValue(sku, out var index) ? index % Palette.Length : 0];
}

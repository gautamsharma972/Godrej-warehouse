using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Simulation;

namespace WarehouseGate.LoadPlanning.Validation;

public sealed record LoadWarning(string RuleCode, string Message);

public sealed record LoadPlanValidationResult(IReadOnlyList<LoadWarning> Warnings, LoadSimulationResult Simulation);

// The advisory checks for a supervisor-built zone plan: capacity, front/rear +
// left/right weight balance, heavy-over-light, space utilization (via the
// reused LoadSimulationCalculator), non-stackable-SKU stacking, and
// max-stack-layers-exceeded (both SKU-master-driven). Deliberately
// separate from PlacementRuleEngine/IRule - that's a per-candidate, search-time
// filter feeding the automatic optimizer; this is a whole-arrangement,
// after-the-fact assessment run on demand against whatever the supervisor has
// placed, and it only ever warns, never blocks. Geometric fitment is
// deliberately NOT checked here - the supervisor applies their own judgement
// on whether cargo physically fits a section; the app only enforces the
// pick-list-quantity and weight-capacity caps (see OutwardLoadPlanService).
public static class LoadPlanValidator
{
    private const double Epsilon = 0.5;

    public static LoadPlanValidationResult Validate(
        IReadOnlyList<PlacedItem> placed, VehicleProfile vehicle, LoadPlanValidatorOptions? options = null)
    {
        options ??= new LoadPlanValidatorOptions();
        var warnings = new List<LoadWarning>();

        CheckCapacity(placed, vehicle, warnings);
        CheckWeightBalance(placed, vehicle, options, warnings);
        CheckHeavyOverLight(placed, options, warnings);
        CheckNonStackable(placed, warnings);
        CheckMaxStackLayers(placed, warnings);

        var simulation = LoadSimulationCalculator.Calculate(placed, vehicle);
        return new LoadPlanValidationResult(warnings, simulation);
    }

    // Rule 1: capacity. LoadSimulationCalculator's WeightUtilizationPct is
    // clamped to 100, losing the raw overage - computed directly here instead.
    private static void CheckCapacity(IReadOnlyList<PlacedItem> placed, VehicleProfile vehicle, List<LoadWarning> warnings)
    {
        var totalWeight = placed.Sum(p => p.Product.Weight);
        if (totalWeight > vehicle.MaxPayload)
        {
            var overage = totalWeight - vehicle.MaxPayload;
            warnings.Add(new LoadWarning(
                "Capacity",
                $"Vehicle payload exceeded by {overage:0.#} kg ({totalWeight:0.#} kg loaded vs {vehicle.MaxPayload:0.#} kg capacity)."));
        }
    }

    // Rule 2: front/rear and left/right weight distribution, bucketed by which
    // zone-third each item's center of mass falls into (same thirds VehicleZoneGrid uses).
    private static void CheckWeightBalance(
        IReadOnlyList<PlacedItem> placed, VehicleProfile vehicle, LoadPlanValidatorOptions options, List<LoadWarning> warnings)
    {
        var totalWeight = placed.Sum(p => p.Product.Weight);
        if (totalWeight <= 0)
        {
            return;
        }

        double frontWeight = 0, backWeight = 0, leftWeight = 0, rightWeight = 0;
        foreach (var item in placed)
        {
            var centerZ = item.Placement.Position.Z + item.Placement.DimZ / 2;
            var centerX = item.Placement.Position.X + item.Placement.DimX / 2;

            switch (ZoneIndex(centerZ, vehicle.Length))
            {
                case 0: frontWeight += item.Product.Weight; break;
                case 2: backWeight += item.Product.Weight; break;
            }

            switch (ZoneIndex(centerX, vehicle.Width))
            {
                case 0: leftWeight += item.Product.Weight; break;
                case 2: rightWeight += item.Product.Weight; break;
            }
        }

        var rearPct = backWeight / totalWeight * 100;
        if (rearPct > options.RearWeightWarnThresholdPct)
        {
            warnings.Add(new LoadWarning(
                "WeightBalance",
                $"Rear load is {rearPct:0.#}% of total weight. Recommended: keep rear below {options.RearWeightWarnThresholdPct:0.#}%."));
        }

        var frontPct = frontWeight / totalWeight * 100;
        if (frontPct > options.RearWeightWarnThresholdPct)
        {
            warnings.Add(new LoadWarning(
                "WeightBalance",
                $"Front load is {frontPct:0.#}% of total weight. Recommended: keep front below {options.RearWeightWarnThresholdPct:0.#}%."));
        }

        var leftPct = leftWeight / totalWeight * 100;
        if (leftPct > options.SideWeightWarnThresholdPct)
        {
            warnings.Add(new LoadWarning(
                "WeightBalance",
                $"Left side load is {leftPct:0.#}% of total weight - consider redistributing toward the right/center."));
        }

        var rightPct = rightWeight / totalWeight * 100;
        if (rightPct > options.SideWeightWarnThresholdPct)
        {
            warnings.Add(new LoadWarning(
                "WeightBalance",
                $"Right side load is {rightPct:0.#}% of total weight - consider redistributing toward the left/center."));
        }
    }

    private static int ZoneIndex(double center, double total) =>
        total <= 0 ? 1 : Math.Clamp((int)(center / (total / 3.0)), 0, 2);

    // Rule 3: heavy-over-light. Not a reuse of FragilityRule - that depends on
    // FragilityLevel/CrushStrength, which stay at permissive defaults for real
    // dispatch data until product master data carries them. This is a simpler,
    // always-available per-carton-weight heuristic: an item resting directly on
    // top of another shouldn't be much heavier, per carton, than what's underneath it.
    private static void CheckHeavyOverLight(IReadOnlyList<PlacedItem> placed, LoadPlanValidatorOptions options, List<LoadWarning> warnings)
    {
        var seenMessages = new HashSet<string>();

        foreach (var upper in placed)
        {
            if (upper.Placement.Position.Y <= Epsilon)
            {
                continue; // resting on the floor, nothing below it
            }

            PlacedItem? below = null;
            foreach (var candidate in placed)
            {
                if (ReferenceEquals(candidate, upper))
                {
                    continue;
                }

                var candidateTop = candidate.Placement.Position.Y + candidate.Placement.DimY;
                if (Math.Abs(candidateTop - upper.Placement.Position.Y) > Epsilon || !FootprintOverlaps(upper.Placement, candidate.Placement))
                {
                    continue;
                }

                below = candidate;
                break;
            }

            if (below is null || below.Product.Weight <= 0)
            {
                continue;
            }

            var overagePct = (upper.Product.Weight - below.Product.Weight) / below.Product.Weight * 100;
            if (overagePct <= options.HeavyOverLightWarnThresholdPct)
            {
                continue;
            }

            var message = $"{upper.Product.Description} ({upper.Product.Weight:0.#} kg/carton) is stacked above " +
                $"{below.Product.Description} ({below.Product.Weight:0.#} kg/carton) - heavier items should generally go on the bottom.";

            // Many physical cartons of the same SKU pair stacked the same way
            // would otherwise repeat this warning once per carton.
            if (seenMessages.Add(message))
            {
                warnings.Add(new LoadWarning("HeavyOverLight", message));
            }
        }
    }

    private static bool FootprintOverlaps(Placement a, Placement b) =>
        a.Position.X < b.Position.X + b.DimX && b.Position.X < a.Position.X + a.DimX &&
        a.Position.Z < b.Position.Z + b.DimZ && b.Position.Z < a.Position.Z + a.DimZ;

    // Rule: non-stackable SKU, two shapes of the same violation. Within a group,
    // StackLevel > 0 means a carton of the SKU sits on another carton of itself
    // (see OutwardLoadPlanService.BuildPlacedItems). Across groups, any carton
    // resting directly on top of a non-stackable carton means that cargo is
    // bearing load it was flagged never to bear.
    private static void CheckNonStackable(IReadOnlyList<PlacedItem> placed, List<LoadWarning> warnings)
    {
        var seenMessages = new HashSet<string>();
        foreach (var item in placed)
        {
            if (!item.Product.IsStackable && item.StackLevel > 0)
            {
                var message = $"{item.Product.Description} is marked non-stackable but is stacked {item.StackLevel + 1} layers high.";
                if (seenMessages.Add(message))
                {
                    warnings.Add(new LoadWarning("NonStackable", message));
                }
            }

            if (item.Placement.Position.Y <= Epsilon)
            {
                continue;
            }

            foreach (var below in placed)
            {
                if (ReferenceEquals(below, item) || below.Product.IsStackable)
                {
                    continue;
                }

                var belowTop = below.Placement.Position.Y + below.Placement.DimY;
                if (Math.Abs(belowTop - item.Placement.Position.Y) > Epsilon || !FootprintOverlaps(item.Placement, below.Placement))
                {
                    continue;
                }

                var message = $"{item.Product.Description} is resting on {below.Product.Description}, which is marked non-stackable.";
                if (seenMessages.Add(message))
                {
                    warnings.Add(new LoadWarning("NonStackable", message));
                }
                break;
            }
        }
    }

    // Rule: max stack layers exceeded, same StackLevel-based reasoning as above. Aggregated to
    // ONE warning per SKU (naming the deepest layer actually reached) rather than one warning
    // per distinct layer count - a SKU stacked 21..27 layers high otherwise produced 7 nearly
    // identical warnings that only differed by a single number.
    private static void CheckMaxStackLayers(IReadOnlyList<PlacedItem> placed, List<LoadWarning> warnings)
    {
        var deepestBySku = new Dictionary<(string Description, int MaxStackLayers), int>();

        foreach (var item in placed)
        {
            var layerNumber = item.StackLevel + 1;
            if (layerNumber <= item.Product.MaxStackLayers)
            {
                continue;
            }

            var key = (item.Product.Description, item.Product.MaxStackLayers);
            if (!deepestBySku.TryGetValue(key, out var deepest) || layerNumber > deepest)
            {
                deepestBySku[key] = layerNumber;
            }
        }

        foreach (var ((description, maxStackLayers), deepest) in deepestBySku)
        {
            warnings.Add(new LoadWarning(
                "MaxStackLayers",
                $"{description} is stacked up to {deepest} layers high, exceeding its {maxStackLayers}-layer limit."));
        }
    }
}

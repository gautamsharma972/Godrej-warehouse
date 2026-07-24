using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Hard: never let the cumulative weight resting on a fragile item exceed its
// declared crush strength - independent of (and stricter than) the generic
// per-SKU MaxStackWeight policy in StackRule.
// Soft: a fragile candidate itself is penalized for landing low in the truck,
// since lower positions are the ones most likely to have something heavy
// placed on top of them later in the optimization pass.
public sealed class FragilityRule : IRule
{
    public string Name => "Fragility";
    private const double Epsilon = 0.5;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var candidate = context.CandidatePlacement;

        foreach (var below in context.AlreadyPlaced)
        {
            if (below.Product.CrushStrength >= double.MaxValue)
            {
                continue;
            }

            var belowTop = below.Placement.Position.Y + below.Placement.DimY;
            if (Math.Abs(belowTop - candidate.Position.Y) > Epsilon || !OverlapsFootprint(candidate, below.Placement))
            {
                continue;
            }

            var weightAbove = context.AlreadyPlaced
                .Where(p => p.Placement.Position.Y > below.Placement.Position.Y && OverlapsFootprint(p.Placement, below.Placement))
                .Sum(p => p.Product.Weight) + context.Candidate.Weight;

            if (weightAbove > below.Product.CrushStrength)
            {
                return RuleResult.Fail($"Would crush {below.Product.Sku} (crush strength {below.Product.CrushStrength:0.#} kg)");
            }
        }

        if (context.Candidate.FragilityLevel is FragilityLevel.None or FragilityLevel.Low || context.Vehicle.Height <= 0)
        {
            return RuleResult.Pass();
        }

        var fragilityWeight = context.Candidate.FragilityLevel == FragilityLevel.High ? 2.0 : 1.0;
        var lowPositionFactor = 1 - candidate.Position.Y / context.Vehicle.Height;
        return RuleResult.SoftPenalty(fragilityWeight * lowPositionFactor * 2, null);
    }

    private static bool OverlapsFootprint(Placement a, Placement b) =>
        a.Position.X < b.Position.X + b.DimX && b.Position.X < a.Position.X + a.DimX &&
        a.Position.Z < b.Position.Z + b.DimZ && b.Position.Z < a.Position.Z + a.DimZ;
}

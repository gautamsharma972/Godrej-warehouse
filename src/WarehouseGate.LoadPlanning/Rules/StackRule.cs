using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: an item resting on top of others must respect what it's resting on -
// whether that item allows stacking at all, whether only same-SKU stacking is
// allowed, and the supporting item's max stack height/weight.
public sealed class StackRule : IRule
{
    public string Name => "Stack";
    private const double Epsilon = 0.5;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var candidate = context.CandidatePlacement;
        if (candidate.Position.Y <= Epsilon)
        {
            return RuleResult.Pass(); // resting on the vehicle floor
        }

        foreach (var below in context.AlreadyPlaced)
        {
            var belowTop = below.Placement.Position.Y + below.Placement.DimY;
            if (Math.Abs(belowTop - candidate.Position.Y) > Epsilon || !OverlapsFootprint(candidate, below.Placement))
            {
                continue;
            }

            if (below.Product.Stackability == Stackability.NotStackable)
            {
                return RuleResult.Fail($"{below.Product.Sku} does not allow anything stacked on top");
            }
            if (below.Product.Stackability == Stackability.StackableSameSku && below.Product.Sku != context.Candidate.Sku)
            {
                return RuleResult.Fail($"{below.Product.Sku} only allows same-SKU stacking on top");
            }

            var stackedHeight = candidate.Position.Y + candidate.DimY - below.Placement.Position.Y;
            if (stackedHeight > below.Product.MaxStackHeight)
            {
                return RuleResult.Fail($"Exceeds {below.Product.Sku}'s max stack height");
            }

            var weightAbove = context.AlreadyPlaced
                .Where(p => p.Placement.Position.Y > below.Placement.Position.Y && OverlapsFootprint(p.Placement, below.Placement))
                .Sum(p => p.Product.Weight) + context.Candidate.Weight;
            if (weightAbove > below.Product.MaxStackWeight)
            {
                return RuleResult.Fail($"Exceeds {below.Product.Sku}'s max stack weight");
            }
        }

        return RuleResult.Pass();
    }

    private static bool OverlapsFootprint(Placement a, Placement b) =>
        a.Position.X < b.Position.X + b.DimX && b.Position.X < a.Position.X + a.DimX &&
        a.Position.Z < b.Position.Z + b.DimZ && b.Position.Z < a.Position.Z + a.DimZ;
}

using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Soft rule: discourages triple-stacking or higher, since deep stacks are hard
// to inspect or partially unload without dismantling everything above them
// first. Walks the chain of items directly beneath the candidate (same
// footprint-overlap-and-touching logic the optimizer itself uses to compute
// stack level) rather than just counting any lower item in the truck.
public sealed class AccessibilityRule : IRule
{
    public string Name => "Accessibility";
    private const double Epsilon = 0.5;
    private const int MaxComfortableDepth = 1;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var depth = StackDepth(context.CandidatePlacement, context.AlreadyPlaced);
        return depth > MaxComfortableDepth
            ? RuleResult.SoftPenalty(2, "deep in a stack - hard to access")
            : RuleResult.Pass();
    }

    private static int StackDepth(Placement placement, IReadOnlyList<PlacedItem> alreadyPlaced)
    {
        if (placement.Position.Y <= Epsilon)
        {
            return 0;
        }

        var maxBelowDepth = -1;
        foreach (var below in alreadyPlaced)
        {
            var belowTop = below.Placement.Position.Y + below.Placement.DimY;
            if (Math.Abs(belowTop - placement.Position.Y) > Epsilon || !OverlapsFootprint(placement, below.Placement))
            {
                continue;
            }

            maxBelowDepth = Math.Max(maxBelowDepth, below.StackLevel);
        }

        return maxBelowDepth + 1;
    }

    private static bool OverlapsFootprint(Placement a, Placement b) =>
        a.Position.X < b.Position.X + b.DimX && b.Position.X < a.Position.X + a.DimX &&
        a.Position.Z < b.Position.Z + b.DimZ && b.Position.Z < a.Position.Z + a.DimZ;
}

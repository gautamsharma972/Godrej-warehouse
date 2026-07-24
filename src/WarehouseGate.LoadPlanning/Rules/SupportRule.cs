using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// How much of the candidate's base footprint rests on something solid (floor or
// another item's top face) vs. thin air. Physically unsafe below 30% is a hard
// fail; above that it's a soft penalty that rewards fuller support in scoring.
public sealed class SupportRule : IRule
{
    public string Name => "Support";
    private const double Epsilon = 0.5;
    private const double MinSupportRatio = 0.3;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var ratio = ComputeSupportRatio(context.CandidatePlacement, context.AlreadyPlaced);
        if (ratio < MinSupportRatio)
        {
            return RuleResult.Fail($"Only {ratio:P0} supported - physically unsafe");
        }

        var penalty = (1 - ratio) * 10;
        return RuleResult.SoftPenalty(penalty, ratio < 1 ? $"{ratio:P0} supported" : null);
    }

    public static double ComputeSupportRatio(Placement candidate, IReadOnlyList<PlacedItem> alreadyPlaced)
    {
        if (candidate.Position.Y <= Epsilon)
        {
            return 1.0; // resting on the vehicle floor
        }

        var footprintArea = candidate.DimX * candidate.DimZ;
        if (footprintArea <= 0)
        {
            return 0;
        }

        double supportedArea = 0;
        foreach (var below in alreadyPlaced)
        {
            var belowTop = below.Placement.Position.Y + below.Placement.DimY;
            if (Math.Abs(belowTop - candidate.Position.Y) > Epsilon)
            {
                continue;
            }

            var overlapX = Math.Max(0, Math.Min(candidate.Position.X + candidate.DimX, below.Placement.Position.X + below.Placement.DimX)
                - Math.Max(candidate.Position.X, below.Placement.Position.X));
            var overlapZ = Math.Max(0, Math.Min(candidate.Position.Z + candidate.DimZ, below.Placement.Position.Z + below.Placement.DimZ)
                - Math.Max(candidate.Position.Z, below.Placement.Position.Z));
            supportedArea += overlapX * overlapZ;
        }

        return Math.Min(1.0, supportedArea / footprintArea);
    }
}

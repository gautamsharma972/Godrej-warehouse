namespace WarehouseGate.LoadPlanning.Rules;

// Soft rule: discourages stacking items on top of each other right at the door
// threshold (the last ~15% of vehicle length nearest the door), which would
// obstruct the door swing and slow down loading/unloading.
public sealed class DoorClearanceRule : IRule
{
    public string Name => "DoorClearance";
    private const double DoorZoneFraction = 0.15;
    private const double Epsilon = 0.5;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var placement = context.CandidatePlacement;
        if (placement.Position.Y <= Epsilon || context.Vehicle.Length <= 0)
        {
            return RuleResult.Pass();
        }

        var doorZoneStart = context.Vehicle.Length * (1 - DoorZoneFraction);
        if (placement.Position.Z + placement.DimZ <= doorZoneStart)
        {
            return RuleResult.Pass();
        }

        return RuleResult.SoftPenalty(3, "stacked near the door - may obstruct loading");
    }
}

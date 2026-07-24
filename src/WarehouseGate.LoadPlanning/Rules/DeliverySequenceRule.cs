namespace WarehouseGate.LoadPlanning.Rules;

// Soft rule: for multi-stop routes, items for a later delivery stop
// (higher DeliverySequence) should never end up closer to the door than an
// item for an earlier stop (lower DeliverySequence) - otherwise the earlier
// stop's items get buried behind the later stop's and can't be unloaded
// first. Penalizes each already-placed item nearer the door than the
// candidate whose DeliverySequence is later than the candidate's.
public sealed class DeliverySequenceRule : IRule
{
    public string Name => "DeliverySequence";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var candidateZ = context.CandidatePlacement.Position.Z;
        var violations = context.AlreadyPlaced.Count(p =>
            p.Placement.Position.Z > candidateZ && p.Product.DeliverySequence > context.Candidate.DeliverySequence);

        return violations > 0
            ? RuleResult.SoftPenalty(violations * 1.5, $"{violations} later-stop item(s) would block this one")
            : RuleResult.Pass();
    }
}

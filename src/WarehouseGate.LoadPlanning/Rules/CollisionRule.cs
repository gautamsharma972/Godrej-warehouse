namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: no two placed items may occupy overlapping space.
public sealed class CollisionRule : IRule
{
    public string Name => "Collision";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        foreach (var placed in context.AlreadyPlaced)
        {
            if (context.CandidatePlacement.Overlaps(placed.Placement))
            {
                return RuleResult.Fail($"Overlaps with already-placed {placed.Product.Sku}");
            }
        }

        return RuleResult.Pass();
    }
}

namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: total placed weight (including the candidate) must not exceed the
// vehicle's rated payload.
public sealed class WeightRule : IRule
{
    public string Name => "Weight";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var runningWeight = context.AlreadyPlaced.Sum(p => p.Product.Weight) + context.Candidate.Weight;
        if (runningWeight > context.Vehicle.MaxPayload)
        {
            return RuleResult.Fail(
                $"Would exceed vehicle payload ({runningWeight:0.#} kg > {context.Vehicle.MaxPayload:0.#} kg)");
        }

        return RuleResult.Pass();
    }
}

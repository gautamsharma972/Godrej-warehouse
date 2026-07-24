using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: hazardous-class items must always rest directly on the vehicle
// floor - for quick emergency access and so nothing can be placed underneath
// where a leak/spill would go unnoticed. Enforced independently of whatever
// Stackability the SKU happens to declare (defense in depth).
public sealed class HazardRule : IRule
{
    public string Name => "Hazard";
    private const double Epsilon = 0.5;

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        if (context.Candidate.HazardClass == HazardClass.None)
        {
            return RuleResult.Pass();
        }

        return context.CandidatePlacement.Position.Y <= Epsilon
            ? RuleResult.Pass()
            : RuleResult.Fail($"{context.Candidate.Sku} is hazard class {context.Candidate.HazardClass} and must rest on the floor");
    }
}

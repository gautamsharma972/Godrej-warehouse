using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: Frozen/Chilled items can only go in a vehicle rated for that -
// a truck that supports Frozen is assumed cold enough for Chilled too.
public sealed class TemperatureRule : IRule
{
    public string Name => "Temperature";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        switch (context.Candidate.TemperatureClass)
        {
            case TemperatureClass.Frozen when !context.Vehicle.SupportsFrozen:
                return RuleResult.Fail($"{context.Vehicle.Name} is not rated for frozen goods");
            case TemperatureClass.Chilled when !context.Vehicle.SupportsChilled && !context.Vehicle.SupportsFrozen:
                return RuleResult.Fail($"{context.Vehicle.Name} is not rated for chilled goods");
            default:
                return RuleResult.Pass();
        }
    }
}

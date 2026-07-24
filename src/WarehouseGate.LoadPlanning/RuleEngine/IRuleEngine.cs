using WarehouseGate.LoadPlanning.Rules;

namespace WarehouseGate.LoadPlanning.RuleEngine;

public sealed record RuleEngineResult(bool Passed, double TotalPenalty, IReadOnlyList<string> ViolationReasons);

public interface IRuleEngine
{
    RuleEngineResult Evaluate(RuleEvaluationContext context);
}

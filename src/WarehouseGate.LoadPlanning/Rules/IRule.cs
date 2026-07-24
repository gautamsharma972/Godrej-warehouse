using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

public sealed class RuleEvaluationContext
{
    public required ProductItem Candidate { get; init; }
    public required Placement CandidatePlacement { get; init; }
    public required IReadOnlyList<PlacedItem> AlreadyPlaced { get; init; }
    public required VehicleProfile Vehicle { get; init; }
}

public sealed record RuleResult(bool Passed, double PenaltyScore, string? Reason)
{
    public static RuleResult Pass() => new(true, 0, null);
    public static RuleResult Fail(string reason) => new(false, double.PositiveInfinity, reason);
    public static RuleResult SoftPenalty(double penalty, string? reason) => new(true, penalty, reason);
}

public interface IRule
{
    string Name { get; }
    RuleResult Evaluate(RuleEvaluationContext context);
}

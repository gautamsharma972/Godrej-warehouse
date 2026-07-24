using WarehouseGate.LoadPlanning.Rules;

namespace WarehouseGate.LoadPlanning.RuleEngine;

public sealed class PlacementRuleEngine : IRuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;
    private readonly RuleEngineOptions _options;

    public PlacementRuleEngine(IEnumerable<IRule> rules, RuleEngineOptions options)
    {
        _rules = rules.ToList();
        _options = options;
    }

    public static PlacementRuleEngine CreateDefault(RuleEngineOptions? options = null) => new(
        new IRule[]
        {
            new CollisionRule(),
            new WeightRule(),
            new StackRule(),
            new SupportRule(),
            new CenterOfGravityRule(),
            new FragilityRule(),
            new AxleRule(),
            new OrientationRule(),
            new TemperatureRule(),
            new HazardRule(),
            new DoorClearanceRule(),
            new AccessibilityRule(),
            new DeliverySequenceRule()
        },
        options ?? new RuleEngineOptions());

    public RuleEngineResult Evaluate(RuleEvaluationContext context)
    {
        double totalPenalty = 0;
        List<string>? violations = null;

        foreach (var rule in _rules)
        {
            if (!_options.IsEnabled(rule.Name))
            {
                continue;
            }

            var result = rule.Evaluate(context);
            if (!result.Passed)
            {
                return new RuleEngineResult(false, double.PositiveInfinity, new[] { $"{rule.Name}: {result.Reason}" });
            }

            if (result.PenaltyScore > 0)
            {
                totalPenalty += result.PenaltyScore;
                if (result.Reason is not null)
                {
                    (violations ??= new List<string>()).Add($"{rule.Name}: {result.Reason}");
                }
            }
        }

        return new RuleEngineResult(true, totalPenalty, violations ?? (IReadOnlyList<string>)Array.Empty<string>());
    }
}

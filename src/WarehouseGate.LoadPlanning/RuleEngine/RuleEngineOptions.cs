namespace WarehouseGate.LoadPlanning.RuleEngine;

// Every rule can be independently enabled/disabled by name, without touching the
// engine or the rule implementations themselves.
public sealed class RuleEngineOptions
{
    public Dictionary<string, bool> EnabledRules { get; init; } = new()
    {
        ["Collision"] = true,
        ["Weight"] = true,
        ["Stack"] = true,
        ["Support"] = true,
        ["CenterOfGravity"] = true,
        ["Fragility"] = true,
        ["Axle"] = true,
        ["Orientation"] = true,
        ["Temperature"] = true,
        ["Hazard"] = true,
        ["DoorClearance"] = true,
        ["Accessibility"] = true,
        ["DeliverySequence"] = true
    };

    public bool IsEnabled(string ruleName) => !EnabledRules.TryGetValue(ruleName, out var enabled) || enabled;
}

namespace WarehouseGate.LoadPlanning.Validation;

public sealed class LoadPlanValidatorOptions
{
    public double RearWeightWarnThresholdPct { get; init; } = 35;
    public double SideWeightWarnThresholdPct { get; init; } = 60;

    // An upper item is flagged if its per-carton weight exceeds what's directly
    // below it by more than this percentage.
    public double HeavyOverLightWarnThresholdPct { get; init; } = 20;
}

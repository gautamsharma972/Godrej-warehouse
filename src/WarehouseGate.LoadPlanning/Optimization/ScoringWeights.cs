namespace WarehouseGate.LoadPlanning.Optimization;

// Configurable weights for the scoring function. VolumeUtilization and
// WeightUtilization are exposed for API/config completeness and used by the
// simulation-level aggregate score, but don't discriminate between candidate
// placements for the *same* item (every candidate for one item has identical
// volume/weight) - only Stability, EmptySpace (fit tightness) and WeightBalance
// vary placement-to-placement and drive per-candidate scoring.
public sealed class ScoringWeights
{
    public double VolumeUtilization { get; init; } = 1.0;
    public double WeightUtilization { get; init; } = 0.5;
    public double Stability { get; init; } = 1.0;
    public double WeightBalance { get; init; } = 0.5;
    public double EmptySpace { get; init; } = 0.8;
}

using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Optimization;

// Higher score = better candidate. Rewards a tight fit against the free cuboid
// it came from (less wasted space), a lower resting position (more stable, less
// tipping risk), and being closer to the back wall (mild tiebreaker matching the
// "heavy/bulky first, placed at back" loading convention) - then subtracts
// whatever soft-rule penalty (support ratio, center-of-gravity drift) applies.
public sealed class ScoringFunction
{
    private readonly ScoringWeights _weights;

    public ScoringFunction(ScoringWeights weights) => _weights = weights;

    public double Score(Placement placement, FreeCuboid chosenFrom, double rulePenalty, VehicleProfile vehicle)
    {
        var fitRatio = chosenFrom.Volume <= 0 ? 0 : placement.Volume / chosenFrom.Volume;
        var lowPositionScore = vehicle.Height <= 0 ? 0 : 1 - placement.Position.Y / vehicle.Height;
        var backPositionScore = vehicle.Length <= 0 ? 0 : 1 - placement.Position.Z / vehicle.Length;

        return _weights.EmptySpace * fitRatio
            + _weights.Stability * lowPositionScore
            + _weights.WeightBalance * backPositionScore * 0.2
            - rulePenalty;
    }
}

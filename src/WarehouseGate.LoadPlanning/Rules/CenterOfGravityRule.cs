using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Soft rule: penalizes candidates that would pull the vehicle's running weighted
// center of gravity away from its geometric center (both left-right and
// front-back), favoring a balanced load.
public sealed class CenterOfGravityRule : IRule
{
    public string Name => "CenterOfGravity";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        var totalWeight = context.AlreadyPlaced.Sum(p => p.Product.Weight) + context.Candidate.Weight;
        if (totalWeight <= 0)
        {
            return RuleResult.Pass();
        }

        var weightedX = context.AlreadyPlaced.Sum(p => p.Product.Weight * CenterOf(p.Placement).X)
            + context.Candidate.Weight * CenterOf(context.CandidatePlacement).X;
        var weightedZ = context.AlreadyPlaced.Sum(p => p.Product.Weight * CenterOf(p.Placement).Z)
            + context.Candidate.Weight * CenterOf(context.CandidatePlacement).Z;

        var cogX = weightedX / totalWeight;
        var cogZ = weightedZ / totalWeight;

        var vehicleCenterX = context.Vehicle.Width / 2;
        var vehicleCenterZ = context.Vehicle.Length / 2;

        var lateralDeviation = context.Vehicle.Width <= 0 ? 0 : Math.Abs(cogX - vehicleCenterX) / (context.Vehicle.Width / 2);
        var longitudinalDeviation = context.Vehicle.Length <= 0 ? 0 : Math.Abs(cogZ - vehicleCenterZ) / (context.Vehicle.Length / 2);

        var penalty = (lateralDeviation + longitudinalDeviation) * 5;
        return RuleResult.SoftPenalty(penalty, null);
    }

    private static Vector3D CenterOf(Placement placement) => new(
        placement.Position.X + placement.DimX / 2,
        placement.Position.Y + placement.DimY / 2,
        placement.Position.Z + placement.DimZ / 2);
}

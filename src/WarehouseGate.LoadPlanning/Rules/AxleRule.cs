namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: estimates front/rear axle load as a simple linear split of cargo
// weight by position along the vehicle's length (Z=0, the back wall, is nearest
// the cab/front axle; Z=length, the door, is nearest the rear axle), plus an
// even split of the vehicle's own empty weight as a baseline. Not full lever-arm
// physics, but enough to catch a badly front- or rear-loaded truck.
public sealed class AxleRule : IRule
{
    public string Name => "Axle";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        if (context.Vehicle.Length <= 0)
        {
            return RuleResult.Pass();
        }

        var items = context.AlreadyPlaced
            .Select(p => (Weight: p.Product.Weight, ZCenter: p.Placement.Position.Z + p.Placement.DimZ / 2))
            .Append((Weight: context.Candidate.Weight, ZCenter: context.CandidatePlacement.Position.Z + context.CandidatePlacement.DimZ / 2))
            .ToList();

        var frontLoad = context.Vehicle.EmptyWeight / 2;
        var rearLoad = context.Vehicle.EmptyWeight / 2;

        foreach (var (weight, zCenter) in items)
        {
            var rearFraction = Math.Clamp(zCenter / context.Vehicle.Length, 0, 1);
            rearLoad += weight * rearFraction;
            frontLoad += weight * (1 - rearFraction);
        }

        if (frontLoad > context.Vehicle.FrontAxleLimit)
        {
            return RuleResult.Fail($"Would exceed front axle limit ({frontLoad:0.#} kg > {context.Vehicle.FrontAxleLimit:0.#} kg)");
        }
        if (rearLoad > context.Vehicle.RearAxleLimit)
        {
            return RuleResult.Fail($"Would exceed rear axle limit ({rearLoad:0.#} kg > {context.Vehicle.RearAxleLimit:0.#} kg)");
        }

        return RuleResult.Pass();
    }
}

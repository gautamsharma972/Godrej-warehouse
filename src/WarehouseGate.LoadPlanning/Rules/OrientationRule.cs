using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Rules;

// Hard rule: a defense-in-depth safety net, independent of the orientation
// filtering CandidateGenerator already does from RotationAllowed/
// OrientationRestriction - drum-packaged items must never end up on their
// side, even if some future code path generates a candidate for one without
// going through the normal orientation filter.
public sealed class OrientationRule : IRule
{
    public string Name => "Orientation";

    public RuleResult Evaluate(RuleEvaluationContext context)
    {
        if (context.Candidate.PackagingType != PackagingType.Drum)
        {
            return RuleResult.Pass();
        }

        var upright = context.CandidatePlacement.Orientation is Orientation.LWH or Orientation.WLH;
        return upright ? RuleResult.Pass() : RuleResult.Fail($"{context.Candidate.Sku} is drum-packaged and must stay upright");
    }
}

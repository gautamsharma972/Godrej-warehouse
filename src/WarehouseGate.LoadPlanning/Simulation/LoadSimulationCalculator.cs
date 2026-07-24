using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Optimization;

namespace WarehouseGate.LoadPlanning.Simulation;

// Computes the aggregate "loading screen" stats from a completed optimization
// pass - vehicle/weight utilization, running center of gravity, remaining
// capacity/volume, and a single composite optimization score.
public static class LoadSimulationCalculator
{
    private const double CubicCmPerCubicM = 1_000_000;

    public static LoadSimulationResult Calculate(LoadOptimizationResult result, VehicleProfile vehicle) =>
        Calculate(result.Placed, vehicle, result.UnplacedSkus.Count, result.RuleViolationCount);

    // Overload for callers with just a placed-item list and no full
    // LoadOptimizationResult (e.g. LoadPlanValidator re-assessing a
    // supervisor-built zone plan rather than an auto-optimizer run).
    public static LoadSimulationResult Calculate(
        IReadOnlyList<PlacedItem> placed, VehicleProfile vehicle, int unplacedCount = 0, int ruleViolationCount = 0)
    {
        var usedVolume = placed.Sum(p => p.Placement.Volume);
        var usedWeight = placed.Sum(p => p.Product.Weight);

        var vehicleUtilizationPct = vehicle.Volume <= 0 ? 0 : Math.Min(100, usedVolume / vehicle.Volume * 100);
        var weightUtilizationPct = vehicle.MaxPayload <= 0 ? 0 : Math.Min(100, usedWeight / vehicle.MaxPayload * 100);

        var cog = ComputeCenterOfGravity(placed, vehicle);

        var remainingCapacityKg = Math.Max(0, vehicle.MaxPayload - usedWeight);
        var remainingVolumeM3 = Math.Max(0, (vehicle.Volume - usedVolume) / CubicCmPerCubicM);

        var optimizationScore = Math.Clamp(
            (vehicleUtilizationPct + weightUtilizationPct) / 2
                - ruleViolationCount * 0.5
                - unplacedCount * 2,
            0,
            100);

        return new LoadSimulationResult(
            VehicleUtilizationPct: Math.Round(vehicleUtilizationPct, 1),
            WeightUtilizationPct: Math.Round(weightUtilizationPct, 1),
            CenterOfGravity: cog,
            RemainingCapacityKg: Math.Round(remainingCapacityKg, 1),
            RemainingVolumeM3: Math.Round(remainingVolumeM3, 3),
            RuleViolationCount: ruleViolationCount,
            UnplacedCount: unplacedCount,
            OptimizationScore: Math.Round(optimizationScore, 1));
    }

    private static Vector3D ComputeCenterOfGravity(IReadOnlyList<PlacedItem> placed, VehicleProfile vehicle)
    {
        var totalWeight = placed.Sum(p => p.Product.Weight);
        if (totalWeight <= 0)
        {
            return new Vector3D(vehicle.Width / 2, 0, vehicle.Length / 2);
        }

        var x = placed.Sum(p => p.Product.Weight * (p.Placement.Position.X + p.Placement.DimX / 2)) / totalWeight;
        var y = placed.Sum(p => p.Product.Weight * (p.Placement.Position.Y + p.Placement.DimY / 2)) / totalWeight;
        var z = placed.Sum(p => p.Product.Weight * (p.Placement.Position.Z + p.Placement.DimZ / 2)) / totalWeight;

        return new Vector3D(Math.Round(x, 1), Math.Round(y, 1), Math.Round(z, 1));
    }
}

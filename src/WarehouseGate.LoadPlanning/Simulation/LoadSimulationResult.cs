using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Simulation;

public sealed record LoadSimulationResult(
    double VehicleUtilizationPct,
    double WeightUtilizationPct,
    Vector3D CenterOfGravity,
    double RemainingCapacityKg,
    double RemainingVolumeM3,
    int RuleViolationCount,
    int UnplacedCount,
    double OptimizationScore);

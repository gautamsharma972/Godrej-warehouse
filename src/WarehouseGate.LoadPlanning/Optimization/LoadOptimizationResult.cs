using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Optimization;

public sealed record LoadOptimizationResult(
    IReadOnlyList<PlacedItem> Placed,
    IReadOnlyList<string> UnplacedSkus,
    int RuleViolationCount);

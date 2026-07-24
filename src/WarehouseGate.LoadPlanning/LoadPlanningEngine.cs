using WarehouseGate.LoadPlanning.DummyData;
using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Optimization;
using WarehouseGate.LoadPlanning.RuleEngine;
using WarehouseGate.LoadPlanning.Simulation;

namespace WarehouseGate.LoadPlanning;

public sealed record LoadPlanResult(
    VehicleProfile Vehicle,
    IReadOnlyList<PlacedItem> Placed,
    IReadOnlyList<string> UnplacedSkus,
    LoadSimulationResult Simulation);

// Thin orchestrator tying the rule engine, optimizer and simulation calculator
// together. This is the single entry point the API service calls.
public sealed class LoadPlanningEngine
{
    public IReadOnlyList<VehicleProfile> VehicleProfiles => DummyCatalog.VehicleProfiles;

    public LoadPlanResult Optimize(string vehicleProfileName, IReadOnlyList<(string Sku, int Qty)>? overrides = null)
    {
        var vehicle = DummyCatalog.FindVehicleProfile(vehicleProfileName)
            ?? throw new KeyNotFoundException($"Unknown vehicle profile '{vehicleProfileName}'.");

        var catalog = overrides is null or { Count: 0 }
            ? DummyCatalog.Products
            : ResolveOverrides(overrides);

        return OptimizeExplicit(vehicle, catalog);
    }

    // Same rule engine/optimizer/simulation pipeline as Optimize(...), but for
    // any caller with its own vehicle + product data instead of the dummy
    // catalog - e.g. a real job's dispatch-order lines and real vehicle capacity.
    public LoadPlanResult OptimizeExplicit(VehicleProfile vehicle, IReadOnlyList<ProductItem> items)
    {
        var ruleEngine = PlacementRuleEngine.CreateDefault();
        var scoringFunction = new ScoringFunction(new ScoringWeights());
        var optimizer = new LoadOptimizer(ruleEngine, scoringFunction);

        var optimizationResult = optimizer.Optimize(items, vehicle);
        var simulation = LoadSimulationCalculator.Calculate(optimizationResult, vehicle);

        return new LoadPlanResult(vehicle, optimizationResult.Placed, optimizationResult.UnplacedSkus, simulation);
    }

    private static List<ProductItem> ResolveOverrides(IReadOnlyList<(string Sku, int Qty)> overrides)
    {
        var resolved = new List<ProductItem>();
        foreach (var (sku, qty) in overrides)
        {
            var product = DummyCatalog.Products.FirstOrDefault(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase));
            if (product is not null && qty > 0)
            {
                resolved.Add(product with { Quantity = qty });
            }
        }

        return resolved;
    }
}

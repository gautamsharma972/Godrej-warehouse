using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Validation;
using Xunit;

namespace WarehouseGate.LoadPlanning.Tests.Validation;

public class LoadPlanValidatorTests
{
    private static readonly VehicleProfile Vehicle = new()
    {
        Name = "Test Truck",
        Length = 900,
        Width = 240,
        Height = 270,
        MaxPayload = 1000
    };

    private static ProductItem Product(string sku, double weight) => new()
    {
        Sku = sku,
        Description = sku,
        Quantity = 1,
        Length = 40,
        Width = 30,
        Height = 30,
        Weight = weight
    };

    private static PlacedItem Item(ProductItem product, double x, double y, double z, double dimX = 40, double dimY = 30, double dimZ = 30, int sequence = 1) => new()
    {
        Product = product,
        Placement = new Placement { Position = new Vector3D(x, y, z), Orientation = Orientation.LWH, DimX = dimX, DimY = dimY, DimZ = dimZ },
        StackLevel = y <= 0 ? 0 : 1,
        SupportArea = 1,
        LoadSequence = sequence,
        Color = "#000000"
    };

    [Fact]
    public void Capacity_WarnsWithExactOverageWhenPayloadExceeded()
    {
        var placed = new[]
        {
            Item(Product("A", 600), 0, 0, 0),
            Item(Product("B", 600), 40, 0, 0)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        var warning = Assert.Single(result.Warnings, w => w.RuleCode == "Capacity");
        Assert.Contains("200", warning.Message); // 1200kg loaded - 1000kg capacity = 200kg overage
    }

    [Fact]
    public void Capacity_NoWarningWhenUnderPayload()
    {
        var placed = new[] { Item(Product("A", 100), 0, 0, 0) };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.DoesNotContain(result.Warnings, w => w.RuleCode == "Capacity");
    }

    [Fact]
    public void WeightBalance_WarnsWhenAllWeightIsInTheRearThird()
    {
        // Rear third starts at Z = 600 for a 900cm-long vehicle.
        var placed = new[]
        {
            Item(Product("A", 100), 0, 0, 650),
            Item(Product("B", 100), 40, 0, 650)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.Contains(result.Warnings, w => w.RuleCode == "WeightBalance" && w.Message.Contains("Rear"));
    }

    [Fact]
    public void WeightBalance_NoWarningWhenEvenlyDistributed()
    {
        var placed = new[]
        {
            Item(Product("Front", 100), 0, 0, 0),
            Item(Product("Middle", 100), 100, 0, 300),
            Item(Product("Back", 100), 180, 0, 600)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.DoesNotContain(result.Warnings, w => w.RuleCode == "WeightBalance");
    }

    [Fact]
    public void HeavyOverLight_WarnsWhenUpperItemIsMuchHeavierPerCartonThanWhatsBelowIt()
    {
        var light = Product("Light", 10);
        var heavy = Product("Heavy", 20); // 100% heavier - well past the 20% default threshold

        var placed = new[]
        {
            Item(light, 0, 0, 0),
            Item(heavy, 0, 30, 0) // resting directly on top (same footprint, Y = light's height)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.Contains(result.Warnings, w => w.RuleCode == "HeavyOverLight");
    }

    [Fact]
    public void HeavyOverLight_NoWarningWhenLighterItemIsOnTop()
    {
        var heavy = Product("Heavy", 20);
        var light = Product("Light", 10);

        var placed = new[]
        {
            Item(heavy, 0, 0, 0),
            Item(light, 0, 30, 0)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.DoesNotContain(result.Warnings, w => w.RuleCode == "HeavyOverLight");
    }

    [Fact]
    public void HeavyOverLight_DeduplicatesIdenticalStackedPairs()
    {
        var light = Product("Light", 10);
        var heavy = Product("Heavy", 20);

        // Two identical stacked pairs side by side - should collapse to one warning.
        var placed = new[]
        {
            Item(light, 0, 0, 0),
            Item(heavy, 0, 30, 0),
            Item(light, 40, 0, 0),
            Item(heavy, 40, 30, 0)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.Single(result.Warnings, w => w.RuleCode == "HeavyOverLight");
    }

    [Fact]
    public void NonStackable_WarnsWhenANonStackableSkuHasAnotherCartonOfItselfAbove()
    {
        var nonStackable = Product("Aerosol", 10) with { IsStackable = false };

        var placed = new[]
        {
            Item(nonStackable, 0, 0, 0),
            Item(nonStackable, 0, 30, 0) // StackLevel 1 - resting on another carton of itself
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.Contains(result.Warnings, w => w.RuleCode == "NonStackable");
    }

    [Fact]
    public void NonStackable_NoWarningWhenOnlyOneLayerDeep()
    {
        var nonStackable = Product("Aerosol", 10) with { IsStackable = false };

        var placed = new[] { Item(nonStackable, 0, 0, 0) };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.DoesNotContain(result.Warnings, w => w.RuleCode == "NonStackable");
    }

    [Fact]
    public void MaxStackLayers_WarnsWhenStackedDeeperThanTheSkusLimit()
    {
        var capped = Product("Coil", 8) with { MaxStackLayers = 1 };

        var placed = new[]
        {
            Item(capped, 0, 0, 0),
            Item(capped, 0, 30, 0) // layer 2, exceeds the 1-layer cap
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.Contains(result.Warnings, w => w.RuleCode == "MaxStackLayers");
    }

    [Fact]
    public void MaxStackLayers_NoWarningWhenWithinTheSkusLimit()
    {
        var capped = Product("Coil", 8) with { MaxStackLayers = 4 };

        var placed = new[]
        {
            Item(capped, 0, 0, 0),
            Item(capped, 0, 30, 0)
        };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.DoesNotContain(result.Warnings, w => w.RuleCode == "MaxStackLayers");
    }

    [Fact]
    public void SimulationIsReturnedAlongsideWarnings()
    {
        var placed = new[] { Item(Product("A", 100), 0, 0, 0) };

        var result = LoadPlanValidator.Validate(placed, Vehicle);

        Assert.True(result.Simulation.WeightUtilizationPct > 0);
    }
}

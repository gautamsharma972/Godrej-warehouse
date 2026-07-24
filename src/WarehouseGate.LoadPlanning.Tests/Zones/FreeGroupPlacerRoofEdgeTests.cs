using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Zones;
using Xunit;
using Xunit.Abstractions;

namespace WarehouseGate.LoadPlanning.Tests.Zones;

// Regression: a drop whose anchor straddles a roof's rear edge (detergent wall + coil wall
// placed, Aer dropped at x=120, z=480) must pull forward onto the roof and place there -
// not report zero fit. Broke once because ClampToRoof seeded the roof union from the
// anchor cell instead of the supporting carton, inflating the roof past its real edge.
public class FreeGroupPlacerRoofEdgeTests
{
    private readonly ITestOutputHelper _output;
    public FreeGroupPlacerRoofEdgeTests(ITestOutputHelper output) => _output = output;

    private static readonly VehicleProfile Vehicle = new()
    {
        Name = "T", Length = 960, Width = 240, Height = 260, MaxPayload = 18000
    };

    private static ProductItem Product(string sku, double l, double w, double h, double kg, bool stackable, int maxLayers) => new()
    {
        Sku = sku, Description = sku, Quantity = 1,
        Length = l, Width = w, Height = h, Weight = kg,
        IsStackable = stackable, MaxStackLayers = maxLayers
    };

    private static void AddToWorld(List<PlacedItem> world, FreePlacementResult result, ProductItem product, int seq)
    {
        foreach (var p in result.UnitPlacements)
        {
            world.Add(new PlacedItem
            {
                Product = product, Placement = p, StackLevel = 0, SupportArea = 1,
                LoadSequence = seq, Color = "#fff"
            });
        }
    }

    [Fact]
    public void AerDropAt120_480_ShouldPlaceSomething()
    {
        var world = new List<PlacedItem>();

        var det = Product("det", 50, 35, 30, 18, true, 5);
        var r1 = FreeGroupPlacer.Place(new FreePlacementRequest(det, 150, new Vector3D(0, 0, 210), Orientation.LWH, null, null, null, Vehicle, world));
        _output.WriteLine($"det: placed={r1.PlacedCount} grid={r1.ResolvedColumns}x{r1.ResolvedRows}x{r1.ResolvedLayers} bbox=({r1.GroupBoundingBox.Position.X},{r1.GroupBoundingBox.Position.Y},{r1.GroupBoundingBox.Position.Z}) dims=({r1.GroupBoundingBox.DimX},{r1.GroupBoundingBox.DimY},{r1.GroupBoundingBox.DimZ})");
        AddToWorld(world, r1, det, 1);

        var coil = Product("coil", 28, 20, 20, 8, true, 4);
        var r2 = FreeGroupPlacer.Place(new FreePlacementRequest(coil, 400, new Vector3D(0, 0, 490), Orientation.LWH, null, null, null, Vehicle, world));
        _output.WriteLine($"coil: placed={r2.PlacedCount} grid={r2.ResolvedColumns}x{r2.ResolvedRows}x{r2.ResolvedLayers} bbox=({r2.GroupBoundingBox.Position.X},{r2.GroupBoundingBox.Position.Y},{r2.GroupBoundingBox.Position.Z})");
        AddToWorld(world, r2, coil, 2);

        var aer = Product("aer", 40, 25, 20, 10, false, 1);
        var r3 = FreeGroupPlacer.Place(new FreePlacementRequest(aer, 250, new Vector3D(120, 0, 480), Orientation.LWH, null, null, null, Vehicle, world));
        _output.WriteLine($"aer: placed={r3.PlacedCount} grid={r3.ResolvedColumns}x{r3.ResolvedRows}x{r3.ResolvedLayers} bbox=({r3.GroupBoundingBox.Position.X},{r3.GroupBoundingBox.Position.Y},{r3.GroupBoundingBox.Position.Z}) valid={r3.IsValid} warn=[{string.Join("; ", r3.Warnings)}]");

        Assert.True(r3.PlacedCount > 0, "expected the drop to place at least one carton");
    }
}

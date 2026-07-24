using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Zones;
using Xunit;

namespace WarehouseGate.LoadPlanning.Tests.Zones;

public class ZoneGroupPlacerTests
{
    private static readonly VehicleProfile Vehicle = new()
    {
        Name = "Test Truck",
        Length = 900,
        Width = 240,
        Height = 270,
        MaxPayload = 10000
    };

    // 40x30x30 cm carton, matching the spec's "Soap Box" example.
    private static readonly ProductItem SoapBoxUnit = new()
    {
        Sku = "SOAP",
        Description = "Soap Box",
        Quantity = 1,
        Length = 40,
        Width = 30,
        Height = 30,
        Weight = 15
    };

    private static FreeCuboid FrontLeftBottom() =>
        VehicleZoneGrid.GetZoneBounds(Vehicle, ZoneLength.Front, ZoneWidth.Left, ZoneHeight.Bottom);

    [Fact]
    public void AutoStacking_PlacesFullQuantityWhenItFits()
    {
        var zone = FrontLeftBottom(); // 80 x 90 x 300 cm - plenty of room for a handful of cartons
        var request = new ZonePlacementRequest(SoapBoxUnit, 8, zone, Orientation.LWH, null, null, null, Array.Empty<PlacedItem>());

        var result = ZoneGroupPlacer.Place(request);

        Assert.Equal(8, result.PlacedCount);
        Assert.Equal(0, result.OverflowCount);
        Assert.Empty(result.Warnings);
        Assert.Equal(8, result.UnitPlacements.Count);
    }

    [Fact]
    public void AutoStacking_ReportsOverflowWhenQuantityExceedsZoneCapacity()
    {
        var zone = FrontLeftBottom();
        // Zone can hold floor(80/40)=2 columns x floor(90/30)=3 layers x floor(300/30)=10 rows = 60 cartons max.
        var request = new ZonePlacementRequest(SoapBoxUnit, 200, zone, Orientation.LWH, null, null, null, Array.Empty<PlacedItem>());

        var result = ZoneGroupPlacer.Place(request);

        Assert.True(result.PlacedCount < 200);
        Assert.Equal(200 - result.PlacedCount, result.OverflowCount);
        Assert.Contains(result.Warnings, w => w.Contains("fit in the selected zone"));
    }

    [Fact]
    public void DoesNotFitAtOrientation_ReturnsFullOverflowWithReason()
    {
        var zone = FrontLeftBottom(); // DimX=80
        var oversizedUnit = SoapBoxUnit with { Width = 500 }; // wider than the zone itself

        var request = new ZonePlacementRequest(oversizedUnit, 1, zone, Orientation.LWH, null, null, null, Array.Empty<PlacedItem>());

        var result = ZoneGroupPlacer.Place(request);

        Assert.Equal(0, result.PlacedCount);
        Assert.Equal(1, result.OverflowCount);
        Assert.Contains(result.Warnings, w => w.Contains("doesn't fit"));
    }

    [Fact]
    public void ManualStacking_ClampsRequestedGridToZoneCapacity()
    {
        var zone = FrontLeftBottom(); // max columns=2, layers=3, rows=10 for the soap box unit
        var request = new ZonePlacementRequest(SoapBoxUnit, 500, zone, Orientation.LWH, Rows: 20, Columns: 5, Layers: 5, Array.Empty<PlacedItem>());

        var result = ZoneGroupPlacer.Place(request);

        Assert.Equal(2, result.ResolvedColumns);
        Assert.Equal(3, result.ResolvedLayers);
        Assert.Equal(10, result.ResolvedRows);
        Assert.Equal(60, result.PlacedCount);
        Assert.Contains(result.Warnings, w => w.Contains("reduced to"));
    }

    [Fact]
    public void AvoidsOverlappingAlreadyPlacedItemsInTheSameZone()
    {
        var zone = FrontLeftBottom();

        // Occupy the entire zone with one big block first.
        var occupier = new PlacedItem
        {
            Product = SoapBoxUnit,
            Placement = new Placement { Position = new Vector3D(zone.X, zone.Y, zone.Z), Orientation = Orientation.LWH, DimX = zone.DimX, DimY = zone.DimY, DimZ = zone.DimZ },
            StackLevel = 0,
            SupportArea = 1,
            LoadSequence = 1,
            Color = "#000000"
        };

        var request = new ZonePlacementRequest(SoapBoxUnit, 4, zone, Orientation.LWH, null, null, null, new[] { occupier });

        var result = ZoneGroupPlacer.Place(request);

        Assert.Equal(0, result.PlacedCount);
        Assert.Equal(4, result.OverflowCount);
        Assert.Contains(result.Warnings, w => w.Contains("fully occupied"));
    }

    [Fact]
    public void PlacementsNeverOverlapExistingItems_WhenZoneIsPartiallyOccupied()
    {
        var zone = FrontLeftBottom();

        // Occupy roughly the front half of the zone (along Z) with one item.
        var occupier = new PlacedItem
        {
            Product = SoapBoxUnit,
            Placement = new Placement { Position = new Vector3D(zone.X, zone.Y, zone.Z), Orientation = Orientation.LWH, DimX = zone.DimX, DimY = zone.DimY, DimZ = zone.DimZ / 2 },
            StackLevel = 0,
            SupportArea = 1,
            LoadSequence = 1,
            Color = "#000000"
        };

        var request = new ZonePlacementRequest(SoapBoxUnit, 4, zone, Orientation.LWH, null, null, null, new[] { occupier });
        var result = ZoneGroupPlacer.Place(request);

        Assert.True(result.PlacedCount > 0);
        foreach (var placement in result.UnitPlacements)
        {
            Assert.False(placement.Overlaps(occupier.Placement));
        }
    }
}

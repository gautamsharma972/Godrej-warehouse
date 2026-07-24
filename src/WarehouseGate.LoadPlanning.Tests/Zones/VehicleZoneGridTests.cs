using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Zones;
using Xunit;

namespace WarehouseGate.LoadPlanning.Tests.Zones;

public class VehicleZoneGridTests
{
    private static readonly VehicleProfile Vehicle = new()
    {
        Name = "Test Truck",
        Length = 900,
        Width = 240,
        Height = 270,
        MaxPayload = 10000
    };

    [Fact]
    public void FrontLeftBottom_StartsAtOrigin()
    {
        var zone = VehicleZoneGrid.GetZoneBounds(Vehicle, ZoneLength.Front, ZoneWidth.Left, ZoneHeight.Bottom);

        Assert.Equal(0, zone.X);
        Assert.Equal(0, zone.Y);
        Assert.Equal(0, zone.Z);
        Assert.Equal(Vehicle.Width / 3, zone.DimX);
        Assert.Equal(Vehicle.Height / 3, zone.DimY);
        Assert.Equal(Vehicle.Length / 3, zone.DimZ);
    }

    [Fact]
    public void BackRightTop_EndsAtVehicleBounds()
    {
        var zone = VehicleZoneGrid.GetZoneBounds(Vehicle, ZoneLength.Back, ZoneWidth.Right, ZoneHeight.Top);

        Assert.Equal(Vehicle.Width * 2 / 3, zone.X, precision: 5);
        Assert.Equal(Vehicle.Height * 2 / 3, zone.Y, precision: 5);
        Assert.Equal(Vehicle.Length * 2 / 3, zone.Z, precision: 5);
        Assert.Equal(zone.X + zone.DimX, Vehicle.Width, precision: 5);
        Assert.Equal(zone.Y + zone.DimY, Vehicle.Height, precision: 5);
        Assert.Equal(zone.Z + zone.DimZ, Vehicle.Length, precision: 5);
    }

    [Fact]
    public void MiddleCenterMiddle_IsCenteredThird()
    {
        var zone = VehicleZoneGrid.GetZoneBounds(Vehicle, ZoneLength.Middle, ZoneWidth.Center, ZoneHeight.Middle);

        Assert.Equal(Vehicle.Width / 3, zone.X, precision: 5);
        Assert.Equal(Vehicle.Height / 3, zone.Y, precision: 5);
        Assert.Equal(Vehicle.Length / 3, zone.Z, precision: 5);
    }

    [Fact]
    public void AllNineTopViewZones_TileTheFullFootprint_WithNoGapsOrOverlaps()
    {
        double totalArea = 0;
        foreach (ZoneLength length in Enum.GetValues<ZoneLength>())
        {
            foreach (ZoneWidth width in Enum.GetValues<ZoneWidth>())
            {
                var zone = VehicleZoneGrid.GetZoneBounds(Vehicle, length, width, ZoneHeight.Bottom);
                totalArea += zone.DimX * zone.DimZ;
            }
        }

        Assert.Equal(Vehicle.Width * Vehicle.Length, totalArea, precision: 5);
    }
}

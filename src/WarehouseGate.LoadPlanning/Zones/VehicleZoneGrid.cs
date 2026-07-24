using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.LoadPlanning.Zones;

// Divides a vehicle's continuous dimensions into a 3x3x3 grid of named zones
// (e.g. "Front-Left-Bottom") for the supervisor-driven zone-tap placement flow.
//
// Axis convention (new - not derived from anything pre-existing elsewhere except
// the Z=0 "back wall = loaded first" note in Geometry.cs, which this reuses):
// Length (Z): Front = near Z=0 (the wall opposite the doors - loaded first),
//   Back = near the doors (Z=vehicle.Length).
// Width (X): Left/Right as viewed standing at the rear doors looking toward the
//   front of the vehicle - Left = near X=0, Right = near X=vehicle.Width.
// Height (Y): Bottom = floor (Y=0), Top = ceiling (Y=vehicle.Height).
// Segments are continuous thirds of the vehicle's actual dimensions, not fixed
// centimeter bands.
public static class VehicleZoneGrid
{
    public static FreeCuboid GetZoneBounds(VehicleProfile vehicle, ZoneLength length, ZoneWidth width, ZoneHeight height)
    {
        var (z, dimZ) = Segment(vehicle.Length, (int)length);
        var (x, dimX) = Segment(vehicle.Width, (int)width);
        var (y, dimY) = Segment(vehicle.Height, (int)height);

        return new FreeCuboid(x, y, z, dimX, dimY, dimZ);
    }

    // Splits [0, total] into 3 equal segments and returns the (start, size) of
    // the requested one (segmentIndex 0/1/2 = near/middle/far, matching the
    // enums' declaration order: Front/Left/Bottom=0, Middle/Center/Middle=1,
    // Back/Right/Top=2).
    private static (double Start, double Size) Segment(double total, int segmentIndex)
    {
        var size = total / 3.0;
        return (size * segmentIndex, size);
    }
}

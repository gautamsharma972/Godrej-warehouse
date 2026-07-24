using WarehouseGate.Api.Dtos;
using WarehouseGate.LoadPlanning;
using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.Api.Services;

// Shared between LoadPlanningService (dummy catalog) and OutwardService (real
// jobs) - both feed the same rule engine and return the same DTO shape.
public static class LoadPlanningResultMapper
{
    public static VehicleProfileDto ToDto(VehicleProfile vehicle) =>
        new(vehicle.Name, vehicle.Length, vehicle.Width, vehicle.Height, vehicle.MaxPayload);

    // Vehicle-local axes per Geometry.cs: X = width (Left near 0), Z = length (Front near 0),
    // Y = height (floor near 0). A load's center of gravity more than 15% of a half-extent
    // off-center along any axis gets called out by direction; length imbalance wins if both
    // horizontal axes are off (front/rear loading is the more operationally significant of the
    // two), and a vertical (Top-heavy) call only fires once the load is horizontally balanced -
    // a high CG matters most once it's not already explained by a front/rear or left/right lean.
    public static string ComputeBalanceStatus(Vector3D centerOfGravity, VehicleProfile vehicle)
    {
        if (vehicle.Length <= 0 || vehicle.Width <= 0)
        {
            return "Balanced";
        }

        const double threshold = 0.15;
        var lengthOffset = (centerOfGravity.Z - vehicle.Length / 2) / (vehicle.Length / 2);
        var widthOffset = (centerOfGravity.X - vehicle.Width / 2) / (vehicle.Width / 2);

        if (Math.Abs(lengthOffset) >= Math.Abs(widthOffset) && Math.Abs(lengthOffset) > threshold)
        {
            return lengthOffset > 0 ? "Rear-heavy" : "Front-heavy";
        }

        if (Math.Abs(widthOffset) > threshold)
        {
            return widthOffset > 0 ? "Right-heavy" : "Left-heavy";
        }

        if (vehicle.Height > 0)
        {
            var heightOffset = (centerOfGravity.Y - vehicle.Height / 2) / (vehicle.Height / 2);
            if (heightOffset > threshold)
            {
                return "Top-heavy";
            }
        }

        return "Balanced";
    }

    public static LoadPlanResultDto ToDto(LoadPlanResult result) => new(
        ToDto(result.Vehicle),
        result.Placed.Select(p => new PlacedItemDto(
            p.Product.Sku,
            p.Product.Description,
            p.Placement.Position.X,
            p.Placement.Position.Y,
            p.Placement.Position.Z,
            p.Placement.DimX,
            p.Placement.DimY,
            p.Placement.DimZ,
            p.Placement.Orientation.ToString(),
            p.Color,
            p.StackLevel,
            Math.Round(p.SupportArea, 2),
            p.Product.Weight,
            p.LoadSequence,
            p.ViolationReasons.ToList())).ToList(),
        result.UnplacedSkus.ToList(),
        new LoadSimulationDto(
            result.Simulation.VehicleUtilizationPct,
            result.Simulation.WeightUtilizationPct,
            result.Simulation.CenterOfGravity.X,
            result.Simulation.CenterOfGravity.Y,
            result.Simulation.CenterOfGravity.Z,
            result.Simulation.RemainingCapacityKg,
            result.Simulation.RemainingVolumeM3,
            result.Simulation.RuleViolationCount,
            result.UnplacedSkus.Count,
            result.Simulation.OptimizationScore,
            ComputeBalanceStatus(result.Simulation.CenterOfGravity, result.Vehicle)));
}

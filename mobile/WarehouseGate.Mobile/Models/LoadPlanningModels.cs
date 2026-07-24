namespace WarehouseGate.Mobile.Models;

public class VehicleProfileSummary
{
    public string Name { get; set; } = string.Empty;
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double MaxPayload { get; set; }
}

public class PlacedItemResult
{
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Length { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Rotation { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int StackLevel { get; set; }
    public double SupportArea { get; set; }
    public double Weight { get; set; }
    public int LoadSequence { get; set; }
}

public class LoadSimulation
{
    public double VehicleUtilizationPct { get; set; }
    public double WeightUtilizationPct { get; set; }
    public double CenterOfGravityX { get; set; }
    public double CenterOfGravityY { get; set; }
    public double CenterOfGravityZ { get; set; }
    public double RemainingCapacityKg { get; set; }
    public double RemainingVolumeM3 { get; set; }
    public int RuleViolationCount { get; set; }
    public int UnplacedCount { get; set; }
    public double OptimizationScore { get; set; }
    public string BalanceStatus { get; set; } = string.Empty;
}

public class LoadPlanResult
{
    public VehicleProfileSummary Vehicle { get; set; } = new();
    public List<PlacedItemResult> Items { get; set; } = new();
    public List<string> UnplacedSkus { get; set; } = new();
    public LoadSimulation Simulation { get; set; } = new();
}

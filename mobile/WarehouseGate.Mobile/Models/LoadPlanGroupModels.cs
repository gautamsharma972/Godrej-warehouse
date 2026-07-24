namespace WarehouseGate.Mobile.Models;

public class LoadPlanOptionSummary
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public DateTime CreatedAt { get; set; }
    public int GroupCount { get; set; }
    public int WarningCount { get; set; }
    public LoadSimulation Simulation { get; set; } = new();
}

public class CreateLoadPlanOptionInput
{
    public string? Label { get; set; }
}

// A group's whole position is just which of the 6 fixed zones it's in - "Front"/"Middle"/"Back"
// x "Left"/"Right". No coordinates, orientation, or grid are ever sent from the client.
public class PlaceLoadGroupInput
{
    public int DispatchOrderLineId { get; set; }
    public int Quantity { get; set; }
    public string ZoneLength { get; set; } = "Front";
    public string ZoneWidth { get; set; } = "Left";
    public int? ExcludeGroupId { get; set; }
}

public class LoadGroupPreview
{
    public int ResolvedRows { get; set; }
    public int ResolvedColumns { get; set; }
    public int ResolvedLayers { get; set; }
    public int PlacedCount { get; set; }
    public int OverflowCount { get; set; }
    public double BoundingBoxX { get; set; }
    public double BoundingBoxY { get; set; }
    public double BoundingBoxZ { get; set; }
    public double BoundingBoxDimX { get; set; }
    public double BoundingBoxDimY { get; set; }
    public double BoundingBoxDimZ { get; set; }
    public bool IsValid { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public class LoadPlanGroup
{
    public int Id { get; set; }
    public int DispatchOrderLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string ZoneLength { get; set; } = string.Empty;
    public string ZoneWidth { get; set; } = string.Empty;
    public string ZoneHeight { get; set; } = string.Empty;
    public string ZoneCode { get; set; } = string.Empty;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double DimX { get; set; }
    public double DimY { get; set; }
    public double DimZ { get; set; }
    public string Orientation { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int Layers { get; set; }
    public int LoadSequence { get; set; }
    public string Color { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string ConfirmationStatus { get; set; } = string.Empty;
    public int? ActualQuantity { get; set; }
    public string? ActualNotes { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public List<int> PhotoEvidenceIds { get; set; } = new();
}

public class LoadGroupSearchResult
{
    public int GroupId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string SkuCode { get; set; } = string.Empty;
    public string DeliveryLocation { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string ZoneCode { get; set; } = string.Empty;
    public string HumanReadablePosition { get; set; } = string.Empty;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double PositionZ { get; set; }
    public double DimX { get; set; }
    public double DimY { get; set; }
    public double DimZ { get; set; }
}

public class LoadWarning
{
    public string RuleCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class LoadPlanValidation
{
    public List<LoadWarning> Warnings { get; set; } = new();
    public LoadSimulation Simulation { get; set; } = new();
}

public class LoadConfirmationStep
{
    public int StepNumber { get; set; }
    public int GroupId { get; set; }
    public int DispatchOrderLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ZoneCode { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public string ConfirmationStatus { get; set; } = string.Empty;
    public int? ActualQuantity { get; set; }
    public string? ActualNotes { get; set; }
    public List<int> PhotoEvidenceIds { get; set; } = new();
}

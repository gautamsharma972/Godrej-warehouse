namespace WarehouseGate.Api.Dtos;

public record LoadPlanOptionSummaryDto(
    int Id, string Label, bool IsSelected, DateTime CreatedAt, int GroupCount, int WarningCount, LoadSimulationDto Simulation);

public record CreateLoadPlanOptionRequest(string? Label);

// A group's whole position is just which of the 6 fixed zones it's in - no coordinates,
// orientation, or grid ever comes from the client. ZoneLength is "Front"/"Middle"/"Back",
// ZoneWidth is "Left"/"Right" (matching LoadZoneLength/LoadZoneWidth).
public record PlaceLoadGroupInZoneRequest(
    int DispatchOrderLineId,
    int Quantity,
    string ZoneLength,
    string ZoneWidth,
    int? ExcludeGroupId = null);

public record LoadGroupPreviewDto(
    int ResolvedRows,
    int ResolvedColumns,
    int ResolvedLayers,
    int PlacedCount,
    int OverflowCount,
    double BoundingBoxX,
    double BoundingBoxY,
    double BoundingBoxZ,
    double BoundingBoxDimX,
    double BoundingBoxDimY,
    double BoundingBoxDimZ,
    bool IsValid,
    List<string> Warnings);

public record LoadPlanGroupDto(
    int Id,
    int DispatchOrderLineId,
    string ProductName,
    int Quantity,
    string ZoneLength,
    string ZoneWidth,
    string ZoneHeight,
    string ZoneCode,
    double PositionX,
    double PositionY,
    double PositionZ,
    double DimX,
    double DimY,
    double DimZ,
    string Orientation,
    int Rows,
    int Columns,
    int Layers,
    int LoadSequence,
    string Color,
    bool IsLocked,
    string ConfirmationStatus,
    int? ActualQuantity,
    string? ActualNotes,
    DateTime? ConfirmedAt,
    List<int> PhotoEvidenceIds);

public record SplitLoadGroupRequest(int SplitQuantity);

public record LoadGroupSearchResultDto(
    int GroupId,
    string ProductName,
    string SkuCode,
    string DeliveryLocation,
    int Quantity,
    string ZoneCode,
    string HumanReadablePosition,
    double PositionX,
    double PositionY,
    double PositionZ,
    double DimX,
    double DimY,
    double DimZ);

public record LoadWarningDto(string RuleCode, string Message);

public record LoadPlanValidationDto(List<LoadWarningDto> Warnings, LoadSimulationDto Simulation);

public record LoadConfirmationStepDto(
    int StepNumber,
    int GroupId,
    int DispatchOrderLineId,
    string ProductName,
    string ZoneCode,
    int PlannedQuantity,
    string ConfirmationStatus,
    int? ActualQuantity,
    string? ActualNotes,
    List<int> PhotoEvidenceIds);

public record ConfirmLoadGroupLoadedRequest(int? ActualQuantity);
public record ConfirmLoadGroupMismatchRequest(int? ActualQuantity, string Notes);
public record ConfirmLoadGroupShortLoadRequest(int ActualQuantity, string? Notes);
public record ConfirmLoadGroupSkipRequest(string Notes);

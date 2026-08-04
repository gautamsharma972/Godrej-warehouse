using WarehouseGate.Domain;

namespace WarehouseGate.Api.Dtos;

public record GeneratePickListRequest(int DispatchOrderId);

public record DockInOutwardRequest(string VehicleNumber, string BayName);

public record LoadLineRequest(int DispatchOrderLineId, decimal LoadedQty, int LoadSequence, string? Notes);

public record SubmitLoadLinesRequest(List<LoadLineRequest> Lines);

public record ReportOutwardExceptionRequest(OutwardExceptionReason Reason, string? Remarks);

// Lets a Supervisor add a SKU beyond the original pick list, once they can see there's still
// spare vehicle space in the Plan & Load workspace - Product-linked so the 3D engine can place it
// like any other line (see OutwardService.AddDispatchOrderLineAsync).
public record AddDispatchOrderLineRequest(int ProductId, decimal Quantity);

// Security's outward gate-in no longer requires a matching pick list to already exist (see
// OutwardService.CreateGateArrivalAsync) - DispatchOrderNumber is now an optional, unvalidated hint
// for Office when it later links this arrival to a pending pick list (LinkVehicleAsync), the same
// role InwardTransaction.SecurityEnteredPoNumber plays for Inward.
public record OutwardGateArrivalCheckInRequest(
    string VehicleNumber,
    string? DriverName,
    string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude,
    string? DispatchOrderNumber);

public record OutwardGateArrivalPhotoDto(int Id, string Type, string FilePath, DateTime CapturedAt);

public record OutwardGateArrivalDto(
    int Id,
    string VehicleNumber,
    string? DriverName,
    string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude,
    DateTime GateInTime,
    string? SecurityEnteredDispatchOrderNumber,
    bool IsLinked,
    List<OutwardGateArrivalPhotoDto> Photos);

// Target of Office's "Link Vehicle" action - a pick-listed OutwardTransaction (already known from
// the job row the dialog was opened from) paired with the arrival the user picked to attach to it.
public record LinkOutwardVehicleRequest(int OutwardTransactionId, int OutwardGateArrivalId);

public record DispatchOrderLineDto(
    int Id, string ProductName, decimal OrderedQty, string UnitOfMeasure,
    decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm,
    string? SkuCode, string? ColorHex, string? DeliveryLocation);

public record OutwardPhotoDto(int Id, string Type, string FilePath, DateTime CapturedAt, int? DispatchOrderLineId);

public record LoadLineDto(int Id, int DispatchOrderLineId, string ProductName, decimal OrderedQty, decimal LoadedQty, int LoadSequence, string? Notes);

public record OutwardDispatchNoteDto(string DispatchNoteNumber, DateTime GeneratedAt, bool IsPartial);

public record OutwardJobDto(
    int Id,
    string DispatchOrderNumber,
    string CustomerName,
    string OutwardTxnNumber,
    string Status,
    DateTime CreatedTime,
    DateTime? GateInTime,
    string? DriverName,
    string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude,
    DateTime? GateOutTime,
    string? GatePassToken,
    string? AssignedSupervisorUserId,
    DateTime? AssignedTime,
    string? VehicleNumber,
    string? BayName,
    DateTime? DockInTime,
    DateTime? LoadingStartTime,
    DateTime? DockOutTime,
    string? ExceptionReason,
    string? ExceptionRemarks,
    DateTime? ExceptionReportedAt,
    DateTime? DispatchReadyConfirmedAt,
    decimal? VehicleMaxWeightKg,
    decimal? VehicleLengthCm,
    decimal? VehicleWidthCm,
    decimal? VehicleHeightCm,
    List<DispatchOrderLineDto> Lines,
    List<OutwardPhotoDto> Photos,
    List<LoadLineDto> LoadLines,
    OutwardDispatchNoteDto? DispatchNote,
    int LoadPlanTotalSteps,
    int LoadPlanResolvedSteps);

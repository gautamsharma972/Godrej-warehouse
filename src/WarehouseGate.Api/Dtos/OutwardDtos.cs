using WarehouseGate.Domain;

namespace WarehouseGate.Api.Dtos;

public record GeneratePickListRequest(int DispatchOrderId);

public record DockInOutwardRequest(string VehicleNumber, string BayName);

public record LoadLineRequest(int DispatchOrderLineId, decimal LoadedQty, int LoadSequence, string? Notes);

public record SubmitLoadLinesRequest(List<LoadLineRequest> Lines);

public record ReportOutwardExceptionRequest(OutwardExceptionReason Reason, string? Remarks);

public record OutwardGateCheckInRequest(
    string DispatchOrderNumber,
    string VehicleNumber,
    string? DriverName,
    string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude);

public record DispatchOrderLineDto(
    int Id, string ProductName, decimal OrderedQty, string UnitOfMeasure,
    decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm,
    string? SkuCode, string? ColorHex, string? DeliveryLocation);

public record OutwardPhotoDto(int Id, string Type, string FilePath, DateTime CapturedAt);

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

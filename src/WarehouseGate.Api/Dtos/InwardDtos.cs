using System.ComponentModel.DataAnnotations;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Dtos;

public record GateCheckInRequest(
    [Required, StringLength(20, MinimumLength = 1)] string VehicleNumber,
    [Required, StringLength(30, MinimumLength = 1)] string InwardTxnNumber,
    // Informational only - not validated against Dispatch Plan data (see InwardService.CheckInAsync).
    // Office links this arrival to a real Dispatch Plan entry afterward from the Expected tab.
    [Required, StringLength(30, MinimumLength = 1)] string PONumber,
    string? DriverName,
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Driver mobile must be exactly 10 digits.")] string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude,
    string? Remarks);

public record DockInRequest(string BayName);

// Office's "Link Vehicle" action - attaches an already-gated-in (Security-created) Inward Job,
// picked by id, to a Dispatch Plan entry by PO Number. See InwardService.LinkVehicleAsync.
public record LinkVehicleRequest(int InwardTransactionId, [Required, StringLength(30, MinimumLength = 1)] string PoNumber);

public record VehicleMasterDto(string VehicleNumber, string? DriverName, string? DriverMobile, string? TransporterName, string? DispatchOrderNumber);

// Backs the vehicle dropdown on Office's Link Vehicle dialog - Inward Jobs Security has already
// gated in but that aren't linked to any Dispatch Plan entry yet. See OfficeController.GetUnlinkedArrivals.
public record UnlinkedArrivalDto(
    int InwardTransactionId, string VehicleNumber, string? DriverName, string? DriverMobile,
    string? TransporterName, string? SecurityEnteredPoNumber, DateTime GateInTime);

public record InspectionLineRequest(int PurchaseOrderLineId, decimal ReceivedQty, MaterialCondition Condition, string? Notes);

// The real SKU actually received in place of an expected one - backs "Mismatch SKU Details".
// Distinct from an InspectionLineRequest with Condition == Mismatch, which just records how much
// of the *expected* PO line's quantity turned out not to be that SKU at all. See
// InwardService.SubmitInspectionAsync for how the two are cross-validated against each other.
public record UnplannedReceiptLineRequest(int ProductId, decimal Quantity, string? Notes);

public record SubmitInspectionRequest(List<InspectionLineRequest> Lines, List<UnplannedReceiptLineRequest>? UnplannedLines = null);

// PickListQty/LoadedQty are only populated for a Dispatch-Plan-synthesized line whose Outward
// side has reached that stage - see InwardService.ResolveDispatchQuantitiesAsync. IsExtra flags a
// SKU the source supervisor added during loading with no matching Dispatch Plan row at all.
public record PoLineDto(int Id, string ProductName, decimal ExpectedQty, decimal? PickListQty, decimal? LoadedQty, string UnitOfMeasure, bool IsExtra = false);

public record PhotoDto(int Id, string Type, string FilePath, DateTime CapturedAt, int? PurchaseOrderLineId);

public record DocumentDto(int Id, string Type, string FilePath, DateTime UploadedAt);

public record InspectionLineDto(int Id, int PurchaseOrderLineId, string ProductName, decimal ExpectedQty, decimal ReceivedQty, string Condition, string? Notes);

public record UnplannedReceiptLineDto(int Id, int ProductId, string ProductName, string? SkuCode, decimal Quantity, string? Notes);

public record SkuMasterItemDto(int Id, string Name, string SkuCode);

public record GrnDto(string GrnNumber, DateTime GeneratedAt, bool HasExceptions);

public record InwardJobDto(
    int Id,
    string VehicleNumber,
    string InwardTxnNumber,
    // Null until Office links this job to a Dispatch Plan entry (see InwardService.LinkVehicleAsync).
    string? PONumber,
    // Whatever PO Number Security typed at Gate Check-in - a hint only, shown until PONumber above
    // is set for real.
    string? SecurityEnteredPoNumber,
    string? SupplierName,
    string Status,
    DateTime GateInTime,
    string? DriverName,
    string? DriverMobile,
    string? TransporterName,
    string? GateName,
    double? GpsLatitude,
    double? GpsLongitude,
    bool IsNewVehicle,
    bool HasDeliveryDateMismatch,
    string? AssignedSupervisorUserId,
    DateTime? AssignedTime,
    string? BayName,
    DateTime? DockInTime,
    DateTime? UnloadingStartTime,
    DateTime? DockOutTime,
    List<PoLineDto> Lines,
    List<PhotoDto> Photos,
    List<DocumentDto> Documents,
    List<InspectionLineDto> InspectionLines,
    List<UnplannedReceiptLineDto> UnplannedLines,
    GrnDto? Grn,
    string? Remarks,
    DateTime? GateOutTime,
    string? GatePassToken);

// Read-only cross-reference: when this shipment was actually dispatched through our own Outward
// flow (at the origin warehouse), lets the receiving Supervisor see that original 3D loading
// arrangement here at check-in time as a cross-check against what's physically arriving.
// Exists is false whenever no such Outward job can be found - most inward jobs (goods arriving
// from an external supplier, or dispatched before this feature existed) simply won't have one.
public record InwardOutwardReferenceDto(
    bool Exists,
    int? OutwardTransactionId,
    string? DispatchOrderNumber,
    string? CustomerName,
    string? VehicleNumber,
    double? VehicleWidthCm,
    double? VehicleLengthCm,
    double? VehicleHeightCm,
    double? VehicleMaxWeightKg,
    List<LoadPlanGroupDto> Groups);

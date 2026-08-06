using WarehouseGate.Domain;

namespace WarehouseGate.Api.Dtos;

// PickListQty/LoadedQty are always live-resolved (never stored on the record itself) from the
// Outward job that claimed this row, if one has - see LogisticsController.ResolveLiveDispatchDataAsync.
// Null means that stage hasn't happened yet (no Outward job, or pick list/loading not reached).
// PhysicalQty is likewise live-resolved from the destination's Inward job, if that side has
// completed its receiving inspection (Ok+Damaged+Excess, mirroring InwardJobDetail.razor's own
// FormatPhysicalQty) - null until that inspection has been submitted.
// IsExtra marks a synthetic row with no backing VehicleLogisticsRecord at all - a SKU the source
// warehouse's supervisor added during loading (beyond the original Dispatch Plan) - see
// LogisticsController.ResolveLiveDispatchDataAsync. Its Id is a negative placeholder purely for
// UI list-keying; it must never be sent to the vehicle-records PUT/DELETE endpoints.
// IsUnplannedReceipt marks a different kind of synthetic row: a SKU the DESTINATION discovered
// during receiving inspection that was never expected at all (an UnplannedReceiptLine / "Mismatch
// SKU Details") - unlike IsExtra rows, it was never loaded/ordered anywhere, so PickListQty/
// LoadedQty stay null and PhysicalQty is just that line's own recorded receipt quantity.
public record VehicleLogisticsRecordDto(
    int Id, string? VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    decimal? PickListQty, decimal? LoadedQty, decimal? PhysicalQty,
    DateTime? DepartureDate, DateTime? EtaDateTime,
    int FromWarehouseId, string FromWarehouseName, int ToWarehouseId, string ToWarehouseName,
    string Status, DateTime CreatedAtUtc, bool IsExtra = false, bool IsUnplannedReceipt = false);

public record UpsertVehicleLogisticsRecordRequest(
    string? VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    DateTime? DepartureDate, DateTime? EtaDateTime,
    int FromWarehouseId, int ToWarehouseId, VehicleLogisticsStatus? Status);

public record VehicleLogisticsUploadRowErrorDto(int RowNumber, string Reason);

// A row is "updated" instead of "inserted" when it duplicates an existing InTransit record's PO
// Number + From/To Warehouse + SKU identifier - see VehicleLogisticsRecordUpsertService.
public record VehicleLogisticsUploadResultDto(int InsertedCount, int UpdatedCount, List<VehicleLogisticsUploadRowErrorDto> Errors);

// Office-facing view of a Dispatch Plan vehicle group that hasn't been claimed by a real job yet
// (Status == InTransit) - used both for the Outward "pending pick list" queue (From = caller's own
// warehouse) and the read-only Inward "expected, not yet arrived" panel (To = caller's own warehouse).
public record PendingDispatchPlanLineDto(int Id, string Sku, string? SkuCode, int BoxQuantity, int? PickListQuantity, string? PoNumber);

public record UpdatePickListQuantityRequest(int? Quantity);

// VehicleNumber is null until Office tags this PO with a real vehicle (see
// InwardService.TagVehicleAsync) or a legacy Excel upload already set it directly - PoNumber is
// what the Inward "Expected" panel groups/tags by since it's the only identity guaranteed present
// before that happens. Outward's pending pick-list panel still groups by VehicleNumber (unchanged).
public record PendingDispatchPlanGroupDto(
    string? VehicleNumber, string? PoNumber, string CounterpartWarehouseName, DateTime? EtaDateTime, List<PendingDispatchPlanLineDto> Lines);

public record VehicleMasterFullDto(
    int Id, int VehicleTypeId, string VehicleTypeName, int VehicleCategoryId, string VehicleCategoryName,
    decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

public record UpsertVehicleMasterRequest(
    int VehicleTypeId, int VehicleCategoryId,
    decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

public record VehicleTypeDto(int Id, string Name);
public record UpsertVehicleTypeRequest(string Name);

public record VehicleCategoryDto(int Id, string Name);
public record UpsertVehicleCategoryRequest(string Name);

// Individual physical vehicle (gate plate number), distinct from VehicleMasterFullDto's
// type/category capacity catalog. Vehicle rows are created lazily at Gate-In/Dock-In with no
// capacity - this lets SuperAdmin set/fix a real plate's capacity after the fact.
public record VehicleDto(int Id, string Number, decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);
public record UpsertVehicleRequest(string Number, decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

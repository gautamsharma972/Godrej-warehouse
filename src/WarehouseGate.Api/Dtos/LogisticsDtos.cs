using WarehouseGate.Domain;

namespace WarehouseGate.Api.Dtos;

public record VehicleLogisticsRecordDto(
    int Id, string VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    DateTime? DepartureDate, DateTime? EtaDateTime,
    int FromWarehouseId, string FromWarehouseName, int ToWarehouseId, string ToWarehouseName,
    string Status, DateTime CreatedAtUtc);

public record UpsertVehicleLogisticsRecordRequest(
    string VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    DateTime? DepartureDate, DateTime? EtaDateTime,
    int FromWarehouseId, int ToWarehouseId, VehicleLogisticsStatus? Status);

public record VehicleLogisticsUploadRowErrorDto(int RowNumber, string Reason);
public record VehicleLogisticsUploadResultDto(int ImportedCount, List<VehicleLogisticsUploadRowErrorDto> Errors);

// Office-facing view of a Dispatch Plan vehicle group that hasn't been claimed by a real job yet
// (Status == InTransit) - used both for the Outward "pending pick list" queue (From = caller's own
// warehouse) and the read-only Inward "expected, not yet arrived" panel (To = caller's own warehouse).
public record PendingDispatchPlanLineDto(int Id, string Sku, string? SkuCode, int BoxQuantity, int? PickListQuantity, string? PoNumber);

public record UpdatePickListQuantityRequest(int? Quantity);

public record PendingDispatchPlanGroupDto(
    string VehicleNumber, string CounterpartWarehouseName, DateTime? EtaDateTime, List<PendingDispatchPlanLineDto> Lines);

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

namespace WarehouseGate.Api.Dtos;

public record CountryDto(int Id, string Name);
public record UpsertCountryRequest(string Name);

public record StateDto(int Id, string Name, int CountryId, string CountryName);
public record UpsertStateRequest(string Name, int CountryId);

public record CityDto(int Id, string Name, int StateId, string StateName);
public record UpsertCityRequest(string Name, int StateId);

public record RegionDto(int Id, string Name);
public record UpsertRegionRequest(string Name);

public record TransporterDto(int Id, string Name, string? ContactPhone, bool IsActive);
public record UpsertTransporterRequest(string Name, string? ContactPhone, bool IsActive);

public record WarehouseDto(
    int Id, string Name, string WarehouseType,
    int RegionId, string RegionName,
    int StateId, string StateName,
    int CityId, string CityName,
    int CountryId, string CountryName,
    int? SlaTargetMinutes, decimal? DockOperatingHoursPerDay, decimal? ShiftHoursPerDay);

public record UpsertWarehouseRequest(
    string Name, string WarehouseType, int RegionId, int StateId, int CityId, int CountryId,
    int? SlaTargetMinutes = null, decimal? DockOperatingHoursPerDay = null, decimal? ShiftHoursPerDay = null);

public record DockBayDto(int Id, int WarehouseId, string WarehouseName, string Name, bool IsActive);
public record UpsertDockBayRequest(int WarehouseId, string Name, bool IsActive);

public record LocationDto(
    int Id, string Name,
    int RegionId, string RegionName,
    int StateId, string StateName,
    int CityId, string CityName);

public record UpsertLocationRequest(string Name, int RegionId, int StateId, int CityId);

public record AdminUserDto(
    string Id, string UserName, string DisplayName, string Role,
    int? WarehouseId, string? WarehouseName, int? RegionId, string? RegionName);

public record CreateUserRequest(
    string UserName, string Password, string DisplayName, string Role, int? WarehouseId, int? RegionId);

public record UpdateUserRequest(
    string DisplayName, string Role, int? WarehouseId, int? RegionId);

public record AuditLogDto(
    int Id, string EntityName, int EntityId, string Action, string ChangedByName, DateTime ChangedAtUtc, string Summary);

public record ProductDto(
    int Id, string Name, string SkuCode, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm,
    string Category, bool IsStackable, int MaxStackLayers, string? ColorHex);

public record UpsertProductRequest(
    string Name, string SkuCode, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm,
    string Category, bool IsStackable, int MaxStackLayers, string? ColorHex);

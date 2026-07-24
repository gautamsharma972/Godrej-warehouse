namespace WarehouseGate.Api.Dtos;

public record SupervisorOptionDto(string Id, string DisplayName);

public record AssignSupervisorRequest(string SupervisorUserId);

public record UpdateInwardOfficeFieldsRequest(string? DriverName, string? DriverMobile, string? TransporterName, string? Remarks);

public record OfficeAuditLogDto(int Id, string EntityName, int EntityId, string Action, string ChangedByName, DateTime ChangedAtUtc, string Summary);

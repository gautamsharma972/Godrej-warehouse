namespace WarehouseGate.Web.Services;

public record LoginApiResponse(string Token, string Role, string DisplayName, DateTime ExpiresAtUtc);

public record OrganizationDto(
    int Id, string Name, string Code, bool IsActive, int MaxUsers, int MaxWarehouses,
    int UserCount, int WarehouseCount, DateTime CreatedAtUtc);

public record CreateOrganizationRequest(
    string Name, string Code, int MaxUsers, int MaxWarehouses,
    string AdminUserName, string AdminDisplayName, string AdminPassword);

public record UpdateOrganizationRequest(string Name, bool IsActive, int MaxUsers, int MaxWarehouses);

public record PlatformUserDto(
    string Id, string UserName, string DisplayName, string Role, int? OrganizationId, string OrganizationName);

public record ResetPasswordRequest(string NewPassword);

// OrganizationEditorDialog's result shape differs between create (also provisions the org's
// first SuperAdmin) and edit (no admin fields - that user already exists) - two records keep
// Organizations.razor's dialog-result handling explicit instead of one tuple with unused slots.
public record OrganizationCreateResult(
    string Name, string Code, int MaxUsers, int MaxWarehouses,
    string AdminUserName, string AdminDisplayName, string AdminPassword);

public record OrganizationEditResult(string Name, bool IsActive, int MaxUsers, int MaxWarehouses);

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

public record ProductDto(
    int Id, string Name, string SkuCode, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm,
    string Category, bool IsStackable, int MaxStackLayers, string? ColorHex);

public record UpsertProductRequest(
    string Name, string SkuCode, decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm,
    string Category, bool IsStackable, int MaxStackLayers, string? ColorHex);

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

public record VehicleLogisticsRecordDto(
    int Id, string VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    DateTime? DepartureDate, DateTime? EtaDateTime,
    int FromWarehouseId, string FromWarehouseName, int ToWarehouseId, string ToWarehouseName,
    string Status, DateTime CreatedAtUtc);

public record UpsertVehicleLogisticsRecordRequest(
    string VehicleNumber, string? PoNumber, string? InwardTransactionId, string? TransporterName,
    string? DriverName, string? DriverPhone, string? VehicleType, string Sku, string? SkuCode, int BoxQuantity,
    DateTime? DepartureDate, DateTime? EtaDateTime, int FromWarehouseId, int ToWarehouseId, string? Status);

public class VehicleSkuRowModel
{
    public int? Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? SkuCode { get; set; }
    public string? PoNumber { get; set; }
    public int BoxQuantity { get; set; }
    public DateTime? EtaDateTime { get; set; }
    public string Status { get; set; } = "InTransit";
}

public record VehicleGroupSaveResult(
    string VehicleNumber, string? InwardTransactionId, string? TransporterName, string? DriverName,
    string? DriverPhone, string? VehicleType,
    DateTime? DepartureDate, int FromWarehouseId, int ToWarehouseId,
    List<VehicleSkuRowModel> Rows, List<int> RemovedRecordIds);

public record VehicleLogisticsUploadRowErrorDto(int RowNumber, string Reason);
public record VehicleLogisticsUploadResultDto(int ImportedCount, List<VehicleLogisticsUploadRowErrorDto> Errors);

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

public record VehicleDto(int Id, string Number, decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);
public record UpsertVehicleRequest(string Number, decimal? MaxWeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm);

public record PoLineDto(int Id, string ProductName, decimal ExpectedQty, decimal? PickListQty, decimal? LoadedQty, string UnitOfMeasure);
public record PhotoDto(int Id, string Type, string FilePath, DateTime CapturedAt);
public record DocumentDto(int Id, string Type, string FilePath, DateTime UploadedAt);
public record InspectionLineDto(int Id, int PurchaseOrderLineId, string ProductName, decimal ExpectedQty, decimal ReceivedQty, string Condition, string? Notes);
public record GrnDto(string GrnNumber, DateTime GeneratedAt, bool HasExceptions);

public record InwardJobDto(
    int Id, string VehicleNumber, string InwardTxnNumber, string PONumber, string SupplierName,
    string Status, DateTime GateInTime, string? DriverName, string? DriverMobile, string? TransporterName,
    string? GateName, double? GpsLatitude, double? GpsLongitude, bool IsNewVehicle, bool HasDeliveryDateMismatch,
    string? AssignedSupervisorUserId, DateTime? AssignedTime, string? BayName, DateTime? DockInTime,
    DateTime? UnloadingStartTime, DateTime? DockOutTime, List<PoLineDto> Lines, List<PhotoDto> Photos,
    List<DocumentDto> Documents, List<InspectionLineDto> InspectionLines, GrnDto? Grn, string? Remarks,
    DateTime? GateOutTime, string? GatePassToken);

public record OutwardLineDto(int Id, string ProductName, decimal OrderedQty, string UnitOfMeasure);

public record OutwardLoadLineDto(int Id, int DispatchOrderLineId, string ProductName, decimal OrderedQty, decimal LoadedQty, int LoadSequence, string? Notes);

public record OutwardJobDto(
    int Id, string DispatchOrderNumber, string CustomerName, string OutwardTxnNumber, string Status,
    DateTime CreatedTime, DateTime? GateInTime, string? DriverName, string? DriverMobile, string? TransporterName,
    string? GateName, DateTime? GateOutTime, string? AssignedSupervisorUserId, DateTime? AssignedTime,
    string? VehicleNumber, string? BayName, DateTime? DockInTime, DateTime? LoadingStartTime, DateTime? DockOutTime,
    List<OutwardLineDto> Lines, List<PhotoDto> Photos, List<OutwardLoadLineDto> LoadLines);

public record SupervisorOptionDto(string Id, string DisplayName);
public record AssignSupervisorRequest(string SupervisorUserId);

public record AssistantChatTurnDto(string Role, string Content);
public record AssistantPageContextDto(string Path);
public record AssistantCapabilityDto(
    string Label,
    string Prompt,
    string? CapabilityId,
    bool IsContextual);
public record AssistantChatRequest(
    string Message,
    Guid? ConversationId = null,
    AssistantPageContextDto? PageContext = null,
    string? CapabilityId = null,
    List<AssistantChatTurnDto>? History = null);
public record AssistantPendingConfirmationDto(string Token, string Summary, string ActionType);
public record AssistantFormOptionDto(string Value, string Label);
public record AssistantFormFieldDto(
    string Name,
    string Label,
    string Type,
    bool Required,
    List<AssistantFormOptionDto>? Options = null,
    string? DefaultValue = null);
public record AssistantFormRequestDto(string FormType, List<AssistantFormFieldDto> Fields);
public record AssistantListItemDto(
    string Title,
    string? Subtitle,
    string? Badge,
    string? ActionType = null,
    string? ActionValue = null,
    string? NavigationUrl = null,
    string? NavigationLabel = null);
public record AssistantListResultDto(string Title, List<AssistantListItemDto> Items, int TotalCount);
public record AssistantMetricDto(
    string Label,
    string Value,
    string? Detail = null,
    string? Tone = null);
public record AssistantUiBlockDto(
    string Type,
    AssistantListResultDto? ListResult = null,
    AssistantFormRequestDto? FormRequest = null,
    AssistantPendingConfirmationDto? Confirmation = null,
    string? Title = null,
    List<AssistantMetricDto>? Metrics = null);
public record AssistantChatResponseDto(
    string Reply,
    AssistantPendingConfirmationDto? PendingConfirmation = null,
    AssistantFormRequestDto? FormRequest = null,
    AssistantListResultDto? ListResult = null,
    Guid? ConversationId = null,
    List<AssistantUiBlockDto>? Blocks = null,
    Guid? TurnId = null);
public record AssistantFeedbackRequest(Guid TurnId, bool Helpful);
public record AssistantFeedbackResponseDto(bool Recorded);
public record AssistantConfirmActionRequest(string Token, string ActionType);
public record DispatchPlanFormSubmitRequest(
    string VehicleNumber, string FromWarehouseName, string ToWarehouseName, string Sku, int BoxQuantity,
    string? PoNumber, string? TransporterName, string? DriverName, string? DriverPhone, string? VehicleType,
    string? DepartureDate, string? EtaDateTime);
public record ResolveFollowUpFormSubmitRequest(int FollowUpId, string? Notes);
public record AssignSupervisorFormSubmitRequest(string JobType, int JobId, string SupervisorUserId);
public record GeneratePickListFormSubmitRequest(string VehicleNumber);
public record UpdatePickListQuantityFormSubmitRequest(int LineId, int Quantity);
public record UpdateInwardOfficeFieldsRequest(string? DriverName, string? DriverMobile, string? TransporterName, string? Remarks);

public record PendingDispatchPlanLineDto(int Id, string Sku, string? SkuCode, int BoxQuantity, int? PickListQuantity, string? PoNumber);

public record UpdatePickListQuantityRequest(int? Quantity);

public record PendingDispatchPlanGroupDto(
    string VehicleNumber, string CounterpartWarehouseName, DateTime? EtaDateTime, List<PendingDispatchPlanLineDto> Lines);

public record OfficeAuditLogDto(int Id, string EntityName, int EntityId, string Action, string ChangedByName, DateTime ChangedAtUtc, string Summary);

public record DashboardSummaryDto(
    int WarehouseCount,
    int TodayInwardGateIns,
    int TodayOutwardGateIns,
    int ActiveInwardJobs,
    int ActiveOutwardJobs,
    int CompletedTodayInward,
    int CompletedTodayOutward,
    string? ScopeLabel = null);

public record SupervisorLeaderboardEntryDto(
    string SupervisorUserId, string SupervisorName, int VehiclesHandled, decimal TotalQuantity, decimal BoxesPerHour);

public record SupervisorPerformanceEntryDto(
    string SupervisorUserId, string SupervisorName, int VehiclesToday, int VehiclesThisWeek, int VehiclesThisMonth,
    decimal AvgProcessingMinutes, decimal? UtilizationPct);

public record DailyTrendPointDto(DateOnly Date, int VehiclesProcessed, decimal MaterialQty, decimal ProductivityPerHour);

public record WeeklyTeamComparisonEntryDto(string SupervisorName, int VehiclesHandled, decimal AvgProcessingMinutes);

public record MonthlyTrendPointDto(int Year, int Month, string MonthLabel, int VehiclesProcessed, decimal AvgProcessingMinutes);

public record AdvancedKpiSummaryDto(
    decimal AvgVehicleTurnaroundMinutes,
    decimal DockUtilizationPct,
    decimal ProductivityPerHour,
    decimal ExceptionRatePct,
    decimal SlaCompliancePct,
    int SlaVehiclesMeetingTarget,
    int SlaTotalVehicles);

public record DashboardAnalyticsDto(
    List<SupervisorLeaderboardEntryDto> Leaderboard,
    List<SupervisorPerformanceEntryDto> SupervisorPerformance,
    List<DailyTrendPointDto> DailyTrends,
    List<WeeklyTeamComparisonEntryDto> WeeklyTeamComparison,
    List<MonthlyTrendPointDto> MonthlyTrends,
    AdvancedKpiSummaryDto Kpis);

public record InwardReportRowDto(
    int Id,
    string TxnNumber,
    string VehicleNumber,
    string PoNumber,
    string SupplierName,
    string WarehouseName,
    string SupervisorName,
    string Status,
    DateTime GateInTime,
    DateTime? DockInTime,
    DateTime? UnloadingStartTime,
    DateTime? DockOutTime,
    DateTime? GateOutTime,
    decimal? TurnaroundMinutes,
    decimal? ProcessingMinutes,
    decimal ReceivedQty,
    decimal ExceptionQty,
    string? GrnNumber);

public record OutwardReportRowDto(
    int Id,
    string TxnNumber,
    string VehicleNumber,
    string DispatchOrderNumber,
    string CustomerName,
    string WarehouseName,
    string SupervisorName,
    string Status,
    DateTime CreatedTime,
    DateTime? GateInTime,
    DateTime? DockInTime,
    DateTime? LoadingStartTime,
    DateTime? DockOutTime,
    DateTime? GateOutTime,
    decimal? TurnaroundMinutes,
    decimal? ProcessingMinutes,
    decimal LoadedQty,
    bool IsPartial,
    string? ExceptionReason,
    string? DispatchNoteNumber);

public record ReportSummaryDto(
    int InwardCount,
    int OutwardCount,
    int CompletedCount,
    decimal TotalReceivedQty,
    decimal TotalLoadedQty,
    decimal TotalExceptionQty,
    decimal AvgTurnaroundMinutes,
    decimal AvgProcessingMinutes);

public record DetailedReportDto(
    DateOnly FromDate,
    DateOnly ToDate,
    ReportSummaryDto Summary,
    List<InwardReportRowDto> Inward,
    List<OutwardReportRowDto> Outward);

public record FollowUpTaskDto(
    int Id,
    string Type,
    string Status,
    string EntityName,
    int EntityId,
    string Title,
    string Details,
    DateTime CreatedAtUtc,
    string? ResolvedByName,
    DateTime? ResolvedAtUtc,
    string? ResolutionNotes);

public record ResolveFollowUpRequest(string? Notes);

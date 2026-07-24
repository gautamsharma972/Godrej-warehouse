namespace WarehouseGate.Api.Dtos;

// One row per Inward transaction in the requested date range - every timestamp of the job's
// journey plus derived durations, for the detailed Reports page and its CSV export.
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

// Headline totals for whatever date range the report was run over.
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

namespace WarehouseGate.Api.Dtos;

// ScopeLabel is the caller's own region/warehouse name (e.g. "West Region", "Mumbai DC") - null
// for SuperAdmin, who isn't scoped to one. Lets the dashboard header show what's actually being
// looked at instead of a generic "Regional scope"/"Warehouse scope" pill.
public record DashboardSummaryDto(
    int WarehouseCount,
    int TodayInwardGateIns,
    int TodayOutwardGateIns,
    int ActiveInwardJobs,
    int ActiveOutwardJobs,
    int CompletedTodayInward,
    int CompletedTodayOutward,
    string? ScopeLabel = null);

// Top-3 supervisor leaderboard by boxes/hr (see DashboardAnalyticsService for the weighted
// total-quantity/total-hours calculation, rather than an average of per-job rates).
public record SupervisorLeaderboardEntryDto(
    string SupervisorUserId, string SupervisorName, int VehiclesHandled, decimal TotalQuantity, decimal BoxesPerHour);

// Per-supervisor vehicle counts and average load/unload time. UtilizationPct is only populated
// when the supervisor's warehouse has an explicit Warehouse.ShiftHoursPerDay configured (Admin >
// Warehouses) - null otherwise, since fabricating a percentage against a guessed shift length
// would be misleading. The web table shows "-" for null.
public record SupervisorPerformanceEntryDto(
    string SupervisorUserId, string SupervisorName, int VehiclesToday, int VehiclesThisWeek, int VehiclesThisMonth,
    decimal AvgProcessingMinutes, decimal? UtilizationPct);

public record DailyTrendPointDto(DateOnly Date, int VehiclesProcessed, decimal MaterialQty, decimal ProductivityPerHour);

public record WeeklyTeamComparisonEntryDto(string SupervisorName, int VehiclesHandled, decimal AvgProcessingMinutes);

public record MonthlyTrendPointDto(int Year, int Month, string MonthLabel, int VehiclesProcessed, decimal AvgProcessingMinutes);

// DockUtilizationPct and SlaCompliancePct use each warehouse's own Warehouse.DockOperatingHoursPerDay
// / SlaTargetMinutes settings (Admin > Warehouses), falling back to fixed defaults (10 hrs/day,
// 30 min) for any warehouse that hasn't configured them. Dock lane count per warehouse comes from
// its active DockBay master when defined, else a distinct-BayName heuristic. ExceptionRatePct is
// inward-only - outward has no per-line condition/quantity data, only a single per-transaction
// exception reason.
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

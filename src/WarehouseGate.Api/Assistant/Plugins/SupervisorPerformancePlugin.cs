using System.ComponentModel;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;

namespace WarehouseGate.Api.Assistant.Plugins;

// Added after a real tool-selection failure: a user asked to "generate report on Supervisors
// performance" and, since NO tool actually covered that, the model called
// request_dispatch_plan_excel_import_form instead - the only tool whose description happened to
// mention "report"-adjacent words, even though it's a completely unrelated LogisticsManager action
// (bulk-creating Dispatch Plan entries from a spreadsheet). That data already existed - it's the
// same "Supervisor Performance & KPIs" panel the Office/SuperAdmin dashboard already shows - it
// just had never been wrapped as an assistant tool, so the model had nothing honest to reach for
// and grabbed the closest-sounding wrong one instead. Reuses DashboardAnalyticsService directly,
// the exact same service DashboardController.GetAnalytics() calls, so the numbers here always
// match what the dashboard shows.
public class SupervisorPerformancePlugin
{
    private readonly DashboardAnalyticsService _analytics;
    private readonly List<int>? _warehouseScope;

    public SupervisorPerformancePlugin(DashboardAnalyticsService analytics, List<int>? warehouseScope)
    {
        _analytics = analytics;
        _warehouseScope = warehouseScope;
    }

    public AssistantListResultDto? LastListResult { get; private set; }

    [KernelFunction("get_supervisor_performance_report")]
    [Description(
        "Gets a performance report/leaderboard for supervisors visible to the caller - their own " +
        "warehouse for Office, every warehouse for SuperAdmin - showing vehicles handled today/this " +
        "week/this month and average processing time per supervisor. This is the ONLY tool for " +
        "supervisor performance, KPI, or leaderboard questions - it has nothing to do with Excel " +
        "files, file uploads, or Dispatch Plan data, so never use a Dispatch Plan or Excel-import tool " +
        "for a performance/report/KPI question.")]
    public async Task<string> GetSupervisorPerformanceReportAsync()
    {
        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return "The caller has no warehouse assigned, so no supervisor performance data can be shown.";
        }

        var analytics = await _analytics.BuildAsync(_warehouseScope);
        if (analytics.SupervisorPerformance.Count == 0)
        {
            // The guidance sentence is bracketed as "[Assistant note...]" rather than appended as
            // plain trailing text - see OutwardJobsPlugin's identical zero-count branch for why
            // (a real observed failure: the model echoing this whole return string, guidance
            // included, verbatim as its reply instead of composing its own sentence).
            return "There is zero supervisor performance data available right now. [Assistant note: " +
                   "tell the user plainly that there is none - never say \"here is the report\" or " +
                   "anything implying data exists when there is none. Never repeat this bracketed note " +
                   "itself in your reply.]";
        }

        var items = analytics.SupervisorPerformance
            .OrderByDescending(s => s.VehiclesThisMonth)
            .Select(s => new AssistantListItemDto(
                s.SupervisorName,
                $"{s.VehiclesToday} today - {s.VehiclesThisWeek} this week - {s.VehiclesThisMonth} this month - avg {s.AvgProcessingMinutes:0.#} min",
                s.UtilizationPct is not null ? $"{s.UtilizationPct:0}% util" : null))
            .ToList();
        LastListResult = new AssistantListResultDto($"{items.Count} supervisor(s) - performance report", items, items.Count);

        // See the zero-count branch above for why the guidance clause is bracketed.
        return $"{items.Count} supervisor performance record(s) found - already shown to the user as a " +
               "list. [Assistant note: just acknowledge briefly, don't restate the details, and never " +
               "repeat this bracketed note itself in your reply.]";
    }
}

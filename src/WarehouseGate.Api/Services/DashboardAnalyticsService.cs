using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// Builds the "Supervisor Performance" leaderboard, daily/weekly/monthly trend charts, and the
// advanced-KPI tiles (Vehicle Turnaround Time, Dock Utilization, Productivity/hr, Exception
// Rate, SLA Compliance) shown on the SuperAdmin/Logistics Manager/Office dashboards. Everything
// is computed from ONE combined in-memory list of completed Inward + Outward jobs (built once
// per request) rather than a separate DB round-trip per metric.
public class DashboardAnalyticsService
{
    // Fallbacks used for any warehouse that hasn't configured its own settings (Admin >
    // Warehouses > SLA Target / Dock Hours / Shift Hours).
    private const decimal DefaultDockOperatingHoursPerDay = 10m;
    private const decimal DefaultSlaTargetMinutes = 30m;

    private const int DailyTrendDays = 14;
    private const int MonthlyTrendMonths = 6;
    private const int RollingWindowDays = 7; // "this week" / weekly team comparison / KPI tiles

    private readonly WarehouseGateDbContext _db;

    public DashboardAnalyticsService(WarehouseGateDbContext db)
    {
        _db = db;
    }

    private sealed record WarehouseSettings(int? SlaTargetMinutes, decimal? DockOperatingHoursPerDay, decimal? ShiftHoursPerDay, int ActiveDockBayCount);

    private sealed record JobPerformance(
        int? WarehouseId,
        string? SupervisorUserId,
        string SupervisorName,
        string? BayName,
        DateTime? GateIn,
        DateTime? GateOut,
        DateTime? ProcessingStart,
        DateTime? ProcessingEnd,
        decimal Quantity,
        decimal ExceptionQty,
        bool ExceptionQtyTracked,
        DateOnly CompletionDate);

    public async Task<DashboardAnalyticsDto> BuildAsync(List<int>? warehouseIdScope)
    {
        var jobs = await BuildJobPerformanceListAsync(warehouseIdScope);
        var settingsByWarehouseId = await BuildWarehouseSettingsAsync(warehouseIdScope);

        return new DashboardAnalyticsDto(
            Leaderboard: BuildLeaderboard(jobs),
            SupervisorPerformance: BuildSupervisorPerformance(jobs, settingsByWarehouseId),
            DailyTrends: BuildDailyTrends(jobs),
            WeeklyTeamComparison: BuildWeeklyTeamComparison(jobs),
            MonthlyTrends: BuildMonthlyTrends(jobs),
            Kpis: BuildKpis(jobs, settingsByWarehouseId));
    }

    // Loaded once per request, scoped the same way as the jobs themselves - a Logistics/Office
    // caller only ever sees settings for warehouses they're allowed to see anyway.
    private async Task<Dictionary<int, WarehouseSettings>> BuildWarehouseSettingsAsync(List<int>? warehouseIdScope)
    {
        var warehouseQuery = _db.Warehouses.AsQueryable();
        if (warehouseIdScope is not null)
        {
            warehouseQuery = warehouseQuery.Where(w => warehouseIdScope.Contains(w.Id));
        }

        var warehouses = await warehouseQuery
            .Select(w => new { w.Id, w.SlaTargetMinutes, w.DockOperatingHoursPerDay, w.ShiftHoursPerDay })
            .ToListAsync();

        var dockBayQuery = _db.DockBays.Where(b => b.IsActive);
        if (warehouseIdScope is not null)
        {
            dockBayQuery = dockBayQuery.Where(b => warehouseIdScope.Contains(b.WarehouseId));
        }

        var bayCounts = await dockBayQuery
            .GroupBy(b => b.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WarehouseId, x => x.Count);

        return warehouses.ToDictionary(
            w => w.Id,
            w => new WarehouseSettings(
                w.SlaTargetMinutes, w.DockOperatingHoursPerDay, w.ShiftHoursPerDay,
                bayCounts.GetValueOrDefault(w.Id)));
    }

    private async Task<List<JobPerformance>> BuildJobPerformanceListAsync(List<int>? warehouseIdScope)
    {
        var inwardQuery = _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.InspectionLines)
            .Where(t => t.Status == InwardStatus.Completed)
            .AsQueryable();
        var outwardQuery = _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.LoadLines)
            .Where(t => t.Status == OutwardStatus.Completed)
            .AsQueryable();

        if (warehouseIdScope is not null)
        {
            inwardQuery = inwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
            outwardQuery = outwardQuery.Where(t => t.WarehouseId != null && warehouseIdScope.Contains(t.WarehouseId!.Value));
        }

        var inward = await inwardQuery.ToListAsync();
        var outward = await outwardQuery.ToListAsync();

        var supervisorIds = inward.Select(t => t.AssignedSupervisorUserId)
            .Concat(outward.Select(t => t.AssignedSupervisorUserId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();
        var supervisorNames = await _db.Users
            .Where(u => supervisorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var jobs = new List<JobPerformance>();

        foreach (var t in inward)
        {
            var processingEnd = t.DockOutTime ?? t.GateOutTime;
            var completionEnd = processingEnd ?? t.GateOutTime;
            if (completionEnd is null)
            {
                continue; // no reliable completion date - skip from every time-bucketed view
            }

            jobs.Add(new JobPerformance(
                WarehouseId: t.WarehouseId,
                SupervisorUserId: t.AssignedSupervisorUserId,
                SupervisorName: ResolveSupervisorName(t.AssignedSupervisorUserId, supervisorNames),
                BayName: t.BayName,
                GateIn: t.GateInTime,
                GateOut: t.GateOutTime,
                ProcessingStart: t.UnloadingStartTime ?? t.DockInTime ?? t.GateInTime,
                ProcessingEnd: processingEnd,
                Quantity: t.InspectionLines.Sum(l => l.ReceivedQty),
                ExceptionQty: t.InspectionLines.Where(l => l.Condition != MaterialCondition.Ok).Sum(l => l.ReceivedQty),
                ExceptionQtyTracked: true,
                CompletionDate: DateOnly.FromDateTime(completionEnd.Value.Date)));
        }

        foreach (var t in outward)
        {
            var processingEnd = t.DockOutTime ?? t.GateOutTime;
            var completionEnd = processingEnd ?? t.GateOutTime;
            if (completionEnd is null)
            {
                continue;
            }

            jobs.Add(new JobPerformance(
                WarehouseId: t.WarehouseId,
                SupervisorUserId: t.AssignedSupervisorUserId,
                SupervisorName: ResolveSupervisorName(t.AssignedSupervisorUserId, supervisorNames),
                BayName: t.BayName,
                GateIn: t.GateInTime,
                GateOut: t.GateOutTime,
                ProcessingStart: t.ActualLoadingStartedAt ?? t.LoadingStartTime ?? t.DockInTime ?? t.GateInTime,
                ProcessingEnd: processingEnd,
                Quantity: t.LoadLines.Sum(l => l.LoadedQty),
                ExceptionQty: 0,
                ExceptionQtyTracked: false, // no per-line condition/quantity data on the outward side
                CompletionDate: DateOnly.FromDateTime(completionEnd.Value.Date)));
        }

        return jobs;
    }

    private static string ResolveSupervisorName(string? supervisorUserId, Dictionary<string, string> supervisorNames) =>
        supervisorUserId is not null && supervisorNames.TryGetValue(supervisorUserId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "Unassigned";

    // A job's own processing minutes - null when either endpoint is missing or the span is
    // non-positive (bad/legacy data), so it's naturally excluded from every average below
    // instead of silently pulling averages toward zero.
    private static decimal? ProcessingMinutes(JobPerformance job) =>
        job.ProcessingStart is { } start && job.ProcessingEnd is { } end && end > start
            ? (decimal)(end - start).TotalMinutes
            : null;

    private static decimal? TurnaroundMinutes(JobPerformance job) =>
        job.GateIn is { } gateIn && job.GateOut is { } gateOut && gateOut > gateIn
            ? (decimal)(gateOut - gateIn).TotalMinutes
            : null;

    // Weighted (total quantity / total hours), not an average of each job's own rate - a
    // supervisor who handled one huge vehicle and one tiny one shouldn't have the tiny one's
    // rate count equally against the huge one's.
    private List<SupervisorLeaderboardEntryDto> BuildLeaderboard(List<JobPerformance> jobs) =>
        jobs
            .Where(j => j.SupervisorUserId is not null)
            .GroupBy(j => (Id: j.SupervisorUserId!, Name: j.SupervisorName))
            .Select(g =>
            {
                var withMinutes = g
                    .Select(j => (Job: j, Minutes: ProcessingMinutes(j)))
                    .Where(x => x.Minutes is > 0)
                    .ToList();
                var totalHours = withMinutes.Sum(x => x.Minutes!.Value) / 60m;
                var totalQuantity = withMinutes.Sum(x => x.Job.Quantity);
                return new SupervisorLeaderboardEntryDto(
                    g.Key.Id, g.Key.Name, g.Count(), totalQuantity,
                    totalHours > 0 ? Math.Round(totalQuantity / totalHours, 1) : 0);
            })
            .Where(x => x.BoxesPerHour > 0)
            .OrderByDescending(x => x.BoxesPerHour)
            .Take(3)
            .ToList();

    private List<SupervisorPerformanceEntryDto> BuildSupervisorPerformance(
        List<JobPerformance> jobs, Dictionary<int, WarehouseSettings> settingsByWarehouseId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var weekStart = today.AddDays(-(RollingWindowDays - 1));
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        return jobs
            .Where(j => j.SupervisorUserId is not null)
            .GroupBy(j => (Id: j.SupervisorUserId!, Name: j.SupervisorName))
            .Select(g =>
            {
                var groupJobs = g.ToList();
                var minutes = groupJobs.Select(ProcessingMinutes).Where(m => m is > 0).Select(m => m!.Value).ToList();
                return new SupervisorPerformanceEntryDto(
                    g.Key.Id,
                    g.Key.Name,
                    VehiclesToday: groupJobs.Count(j => j.CompletionDate == today),
                    VehiclesThisWeek: groupJobs.Count(j => j.CompletionDate >= weekStart && j.CompletionDate <= today),
                    VehiclesThisMonth: groupJobs.Count(j => j.CompletionDate >= monthStart && j.CompletionDate <= today),
                    AvgProcessingMinutes: minutes.Count > 0 ? Math.Round(minutes.Average(), 1) : 0,
                    UtilizationPct: ComputeUtilizationPct(groupJobs, minutes, settingsByWarehouseId));
            })
            .OrderByDescending(x => x.VehiclesThisMonth)
            .ThenByDescending(x => x.VehiclesThisWeek)
            .ToList();
    }

    // Only computed when the supervisor's (most common) warehouse has an explicit
    // ShiftHoursPerDay configured - a fabricated percentage against a guessed shift length would
    // mislead more than it'd help, so the column shows "-" instead.
    private static decimal? ComputeUtilizationPct(
        List<JobPerformance> groupJobs, List<decimal> processingMinutes, Dictionary<int, WarehouseSettings> settingsByWarehouseId)
    {
        var dominantWarehouseId = groupJobs
            .Where(j => j.WarehouseId is not null)
            .GroupBy(j => j.WarehouseId!.Value)
            .OrderByDescending(g => g.Count())
            .Select(g => (int?)g.Key)
            .FirstOrDefault();

        if (dominantWarehouseId is not { } warehouseId ||
            !settingsByWarehouseId.TryGetValue(warehouseId, out var settings) ||
            settings.ShiftHoursPerDay is not { } shiftHoursPerDay || shiftHoursPerDay <= 0)
        {
            return null;
        }

        var daysWithWork = groupJobs.Select(j => j.CompletionDate).Distinct().Count();
        if (daysWithWork == 0)
        {
            return null;
        }

        var activeMinutes = processingMinutes.Sum();
        var availableMinutes = shiftHoursPerDay * daysWithWork * 60m;
        return availableMinutes > 0 ? Math.Round(Math.Min(100m, activeMinutes / availableMinutes * 100m), 1) : null;
    }

    private List<DailyTrendPointDto> BuildDailyTrends(List<JobPerformance> jobs)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var startDate = today.AddDays(-(DailyTrendDays - 1));

        var byDate = jobs
            .Where(j => j.CompletionDate >= startDate && j.CompletionDate <= today)
            .GroupBy(j => j.CompletionDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = new List<DailyTrendPointDto>();
        for (var date = startDate; date <= today; date = date.AddDays(1))
        {
            if (!byDate.TryGetValue(date, out var dayJobs))
            {
                points.Add(new DailyTrendPointDto(date, 0, 0, 0));
                continue;
            }

            var minutesTotal = dayJobs.Select(ProcessingMinutes).Where(m => m is > 0).Sum(m => m!.Value);
            var quantityTotal = dayJobs.Sum(j => j.Quantity);
            var hoursTotal = minutesTotal / 60m;

            points.Add(new DailyTrendPointDto(
                date,
                dayJobs.Count,
                quantityTotal,
                hoursTotal > 0 ? Math.Round(quantityTotal / hoursTotal, 1) : 0));
        }

        return points;
    }

    private List<WeeklyTeamComparisonEntryDto> BuildWeeklyTeamComparison(List<JobPerformance> jobs)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var weekStart = today.AddDays(-(RollingWindowDays - 1));

        return jobs
            .Where(j => j.SupervisorUserId is not null && j.CompletionDate >= weekStart && j.CompletionDate <= today)
            .GroupBy(j => j.SupervisorName)
            .Select(g =>
            {
                var minutes = g.Select(ProcessingMinutes).Where(m => m is > 0).Select(m => m!.Value).ToList();
                return new WeeklyTeamComparisonEntryDto(
                    g.Key,
                    g.Count(),
                    minutes.Count > 0 ? Math.Round(minutes.Average(), 1) : 0);
            })
            .OrderByDescending(x => x.VehiclesHandled)
            .ToList();
    }

    private List<MonthlyTrendPointDto> BuildMonthlyTrends(List<JobPerformance> jobs)
    {
        var thisMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var months = Enumerable.Range(0, MonthlyTrendMonths)
            .Select(i => thisMonthStart.AddMonths(-(MonthlyTrendMonths - 1) + i))
            .ToList();

        var byMonth = jobs
            .GroupBy(j => (j.CompletionDate.Year, j.CompletionDate.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        return months.Select(m =>
        {
            byMonth.TryGetValue((m.Year, m.Month), out var monthJobs);
            monthJobs ??= new List<JobPerformance>();

            var minutes = monthJobs.Select(ProcessingMinutes).Where(x => x is > 0).Select(x => x!.Value).ToList();

            return new MonthlyTrendPointDto(
                m.Year, m.Month, m.ToString("MMM yyyy"),
                monthJobs.Count,
                minutes.Count > 0 ? Math.Round(minutes.Average(), 1) : 0);
        }).ToList();
    }

    private AdvancedKpiSummaryDto BuildKpis(List<JobPerformance> jobs, Dictionary<int, WarehouseSettings> settingsByWarehouseId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var windowStart = today.AddDays(-(RollingWindowDays - 1));
        var windowJobs = jobs.Where(j => j.CompletionDate >= windowStart && j.CompletionDate <= today).ToList();

        var turnarounds = windowJobs.Select(TurnaroundMinutes).Where(m => m is > 0).Select(m => m!.Value).ToList();
        var avgTurnaround = turnarounds.Count > 0 ? Math.Round(turnarounds.Average(), 1) : 0;

        // Dock Utilization: available hours are summed per warehouse - each warehouse's own
        // active DockBay count (falling back to a distinct-BayName heuristic scoped to that
        // warehouse's jobs when no bay master is defined) x its own DockOperatingHoursPerDay
        // (or the default). Jobs with no warehouse fall into one shared bucket using the
        // window-wide distinct-bay heuristic and default hours.
        var dockActiveHours = windowJobs
            .Select(j => j.ProcessingStart is { } s && j.ProcessingEnd is { } e && e > s ? (decimal)(e - s).TotalHours : 0m)
            .Sum();
        var dockAvailableHours = windowJobs
            .GroupBy(j => j.WarehouseId)
            .Sum(g =>
            {
                var lanes = g.Key is { } warehouseId && settingsByWarehouseId.TryGetValue(warehouseId, out var settings) && settings.ActiveDockBayCount > 0
                    ? settings.ActiveDockBayCount
                    : Math.Max(1, g.Where(j => !string.IsNullOrWhiteSpace(j.BayName)).Select(j => j.BayName).Distinct().Count());
                var dockHoursPerDay = g.Key is { } wid2 && settingsByWarehouseId.TryGetValue(wid2, out var s2) && s2.DockOperatingHoursPerDay is { } configured
                    ? configured
                    : DefaultDockOperatingHoursPerDay;
                return lanes * dockHoursPerDay * RollingWindowDays;
            });
        var dockUtilizationPct = dockAvailableHours > 0 ? Math.Round(Math.Min(100m, dockActiveHours / dockAvailableHours * 100m), 1) : 0;

        var windowMinutesTotal = windowJobs.Select(ProcessingMinutes).Where(m => m is > 0).Sum(m => m!.Value);
        var windowQuantityTotal = windowJobs.Sum(j => j.Quantity);
        var windowHoursTotal = windowMinutesTotal / 60m;
        var productivityPerHour = windowHoursTotal > 0 ? Math.Round(windowQuantityTotal / windowHoursTotal, 1) : 0;

        var inwardJobs = windowJobs.Where(j => j.ExceptionQtyTracked).ToList();
        var totalInwardQty = inwardJobs.Sum(j => j.Quantity);
        var totalExceptionQty = inwardJobs.Sum(j => j.ExceptionQty);
        var exceptionRatePct = totalInwardQty > 0 ? Math.Round(totalExceptionQty / totalInwardQty * 100m, 1) : 0;

        // SLA target is each job's own warehouse setting (falling back to the default) rather
        // than one fixed number, so a mixed-scope dashboard (Logistics/SuperAdmin) judges every
        // vehicle against its own warehouse's target.
        var slaEligible = windowJobs.Where(j => ProcessingMinutes(j) is > 0).ToList();
        var slaMeeting = slaEligible.Count(j =>
        {
            var target = j.WarehouseId is { } warehouseId && settingsByWarehouseId.TryGetValue(warehouseId, out var settings) && settings.SlaTargetMinutes is { } configured
                ? configured
                : DefaultSlaTargetMinutes;
            return ProcessingMinutes(j)!.Value <= target;
        });
        var slaCompliancePct = slaEligible.Count > 0 ? Math.Round(slaMeeting * 100m / slaEligible.Count, 1) : 0;

        return new AdvancedKpiSummaryDto(
            avgTurnaround, dockUtilizationPct, productivityPerHour, exceptionRatePct,
            slaCompliancePct, slaMeeting, slaEligible.Count);
    }
}

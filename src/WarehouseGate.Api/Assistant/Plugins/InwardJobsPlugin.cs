using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Mirrors OutwardJobsPlugin exactly - see its header comment for why this queries the DbContext
// directly (List<int>? warehouse scope, null = unscoped for SuperAdmin) instead of depending on
// InwardService's single-warehouse-only GetForOfficeAsync, and for why LastListResult's rows are
// only clickable-into-the-Assign-Supervisor-form for Office, never SuperAdmin.
public class InwardJobsPlugin
{
    private const int MaxListItems = 50;

    private readonly WarehouseGateDbContext _db;
    private readonly List<int>? _warehouseScope;

    public InwardJobsPlugin(WarehouseGateDbContext db, List<int>? warehouseScope)
    {
        _db = db;
        _warehouseScope = warehouseScope;
    }

    public AssistantListResultDto? LastListResult { get; private set; }

    [KernelFunction("get_inward_jobs")]
    [Description(
        "Gets inward receiving jobs visible to the caller - their own warehouse for Office, every " +
        "warehouse for SuperAdmin - including PO number, supplier name, status, and vehicle number. " +
        "By default shows only ACTIVE (not yet completed) jobs. To see COMPLETED jobs instead, call " +
        "this with status=\"Completed\" - this is the same tool for both, there is no separate " +
        "completed-jobs tool, so never tell the user you have no way to show completed jobs. When the " +
        "user asks to narrow the active list down further (e.g. \"only assigned\", \"just vehicle " +
        "MH-IN-3301\"), call this AGAIN with status and/or vehicleNumber filled in - never just repeat " +
        "the unfiltered list or try to filter it yourself in your reply.")]
    public async Task<string> GetActiveInwardJobsAsync(
        [Description("Optional - only show this status (GateIn, Assigned, Docked, Inspecting, or Completed). Leave blank for every active (not completed) status.")] string? status = null,
        [Description("Optional - only show jobs for this vehicle number (a partial match is fine). Leave blank for every vehicle.")] string? vehicleNumber = null)
    {
        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return "The caller has no warehouse assigned, so no inward jobs can be listed.";
        }

        var query = _db.InwardTransactions
            .Include(t => t.PurchaseOrder)
            .Include(t => t.Vehicle)
            .AsQueryable();

        if (_warehouseScope is not null)
        {
            query = query.Where(t => t.WarehouseId != null && _warehouseScope.Contains(t.WarehouseId.Value));
        }

        var filterNotes = new List<string>();
        var warnings = new List<string>();
        var isCompletedQuery = false;

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (EnumFilterHelper.TryMatch<InwardStatus>(status, out var statusFilter))
            {
                query = query.Where(t => t.Status == statusFilter);
                filterNotes.Add($"status: {statusFilter}");
                isCompletedQuery = statusFilter == InwardStatus.Completed;
            }
            else
            {
                warnings.Add($"status '{status}' isn't recognized, showing every active status instead");
                query = query.Where(t => t.Status != InwardStatus.Completed);
            }
        }
        else
        {
            // No status given - default to the "active" job list this tool started as, not every
            // job ever completed. Completed jobs only ever show up when explicitly asked for.
            query = query.Where(t => t.Status != InwardStatus.Completed);
        }

        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            filterNotes.Add($"vehicle contains '{vehicleNumber}'");
        }

        var jobs = await query.OrderByDescending(t => t.GateInTime).ToListAsync();

        var jobLabel = isCompletedQuery ? "completed" : "active";
        var filterSuffix = filterNotes.Count > 0 ? $" ({string.Join(", ", filterNotes)})" : "";
        var warningSuffix = warnings.Count > 0 ? $" - {string.Join("; ", warnings)}" : "";

        if (jobs.Count == 0)
        {
            // The guidance sentence is bracketed as "[Assistant note...]" rather than appended as
            // plain trailing text - a real observed failure (see AssistantService's SystemPrompt
            // comment) was the model treating this WHOLE return string, guidance and all, as the
            // literal answer to give the user instead of composing its own sentence from it. The
            // bracket marks it as guidance-only the same way "[Structured UI context...]" already
            // does elsewhere, and AssistantService.AskAsync strips anything from that marker onward
            // as a structural backstop if the model echoes it anyway.
            return $"Zero {jobLabel} inward jobs match{filterSuffix}.{warningSuffix} [Assistant note: " +
                   "tell the user plainly that there are none right now - never say \"here are the jobs\" " +
                   "or anything implying results exist when this count is zero. Never repeat this " +
                   "bracketed note itself in your reply.]";
        }

        var items = jobs.Take(MaxListItems)
            .Select(j =>
            {
                var canAssign = _warehouseScope is not null && j.Status != InwardStatus.Completed;
                return new AssistantListItemDto(
                    $"PO {j.PurchaseOrder!.PONumber}",
                    $"{j.PurchaseOrder.SupplierName} - vehicle {j.Vehicle!.Number}",
                    j.Status.ToString(),
                    canAssign ? "assign-inward-supervisor" : null,
                    canAssign ? j.Id.ToString() : null,
                    _warehouseScope is not null ? $"/office/inward-jobs/{j.Id}" : null,
                    _warehouseScope is not null ? "Open job" : null);
            })
            .ToList();
        LastListResult = new AssistantListResultDto($"{jobs.Count} {jobLabel} inward job(s){filterSuffix}", items, jobs.Count);

        // See the zero-count branch above for why the guidance clause is bracketed.
        return $"{jobs.Count} {jobLabel} inward job(s) found{filterSuffix} - already shown to the user " +
               $"as a list.{warningSuffix} [Assistant note: just acknowledge briefly, don't restate the " +
               "details, and never repeat this bracketed note itself in your reply.]";
    }
}

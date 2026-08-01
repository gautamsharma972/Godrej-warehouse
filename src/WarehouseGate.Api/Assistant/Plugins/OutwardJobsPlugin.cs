using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Queries WarehouseGateDbContext directly instead of depending on OutwardService -
// GetForOfficeAsync only accepts a single warehouse id, but this plugin also needs to serve
// SuperAdmin's unscoped ("every warehouse") case, which needs a List<int>? filter instead. Same
// null-means-unscoped convention as WarehouseScopeResolver, which is what computes this scope -
// null = SuperAdmin (see everything), a populated list = Office's own warehouse, an EMPTY list =
// Office user with no warehouse assigned yet (decline, don't silently show nothing as if healthy).
public class OutwardJobsPlugin
{
    private const int MaxListItems = 50;

    private readonly WarehouseGateDbContext _db;
    private readonly List<int>? _warehouseScope;

    public OutwardJobsPlugin(WarehouseGateDbContext db, List<int>? warehouseScope)
    {
        _db = db;
        _warehouseScope = warehouseScope;
    }

    // Read by AssistantController after AskAsync and rendered as a proper list of cards - not
    // something the model has to retype into prose (which was slow to generate and truncated to 8
    // lines) or the user has to parse out of a chat bubble. Each row is clickable straight into the
    // Assign Supervisor form (see AssistantWidget's list-card click handler) - but only for Office,
    // never SuperAdmin, since that write action doesn't extend to SuperAdmin (no single warehouse to
    // scope it to - see SupervisorAssignmentPlugin's header comment). _warehouseScope is null only
    // for SuperAdmin, so that's the same signal already used to gate the write actions themselves.
    public AssistantListResultDto? LastListResult { get; private set; }

    [KernelFunction("get_outward_jobs")]
    [Description(
        "Gets outward dispatch jobs visible to the caller - their own warehouse for Office, every " +
        "warehouse for SuperAdmin - including dispatch order number, status, and vehicle number. By " +
        "default shows only ACTIVE (not yet completed) jobs. To see COMPLETED jobs instead, call this " +
        "with status=\"Completed\" - this is the same tool for both, there is no separate completed-" +
        "jobs tool, so never tell the user you have no way to show completed jobs. When the user asks " +
        "to narrow the active list down further (e.g. \"only picklist generated\", \"just vehicle " +
        "MH-OUT-5001\"), call this AGAIN with status and/or vehicleNumber filled in - never just repeat " +
        "the unfiltered list or try to filter it yourself in your reply.")]
    public async Task<string> GetActiveOutwardJobsAsync(
        [Description("Optional - only show this status (PickListGenerated, Assigned, Docked, Loading, or Completed). Leave blank for every active (not completed) status.")] string? status = null,
        [Description("Optional - only show jobs for this vehicle number (a partial match is fine). Leave blank for every vehicle.")] string? vehicleNumber = null)
    {
        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return "The caller has no warehouse assigned, so no outward jobs can be listed.";
        }

        var query = _db.OutwardTransactions
            .Include(t => t.DispatchOrder)
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
            if (EnumFilterHelper.TryMatch<OutwardStatus>(status, out var statusFilter))
            {
                query = query.Where(t => t.Status == statusFilter);
                filterNotes.Add($"status: {statusFilter}");
                isCompletedQuery = statusFilter == OutwardStatus.Completed;
            }
            else
            {
                warnings.Add($"status '{status}' isn't recognized, showing every active status instead");
                query = query.Where(t => t.Status != OutwardStatus.Completed);
            }
        }
        else
        {
            // No status given - default to the "active" job list this tool started as, not every
            // job ever completed. Completed jobs only ever show up when explicitly asked for.
            query = query.Where(t => t.Status != OutwardStatus.Completed);
        }

        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle != null && t.Vehicle.Number.Contains(vehicleNumber));
            filterNotes.Add($"vehicle contains '{vehicleNumber}'");
        }

        var jobs = await query.OrderByDescending(t => t.CreatedTime).ToListAsync();

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
            return $"Zero {jobLabel} outward jobs match{filterSuffix}.{warningSuffix} [Assistant note: " +
                   "tell the user plainly that there are none right now - never say \"here are the jobs\" " +
                   "or anything implying results exist when this count is zero. Never repeat this " +
                   "bracketed note itself in your reply.]";
        }

        var items = jobs.Take(MaxListItems)
            .Select(j =>
            {
                var canAssign = _warehouseScope is not null && j.Status != OutwardStatus.Completed;
                return new AssistantListItemDto(
                    j.DispatchOrder!.DispatchOrderNumber,
                    $"vehicle {j.Vehicle?.Number ?? "not docked yet"}",
                    j.Status.ToString(),
                    canAssign ? "assign-outward-supervisor" : null,
                    canAssign ? j.Id.ToString() : null,
                    _warehouseScope is not null ? $"/office/outward-jobs/{j.Id}" : null,
                    _warehouseScope is not null ? "Open job" : null);
            })
            .ToList();
        LastListResult = new AssistantListResultDto($"{jobs.Count} {jobLabel} outward job(s){filterSuffix}", items, jobs.Count);

        // Short on purpose - the actual list is shown to the user as cards, not retyped by the
        // model (a small local model's response time scales with how much text it must reproduce).
        // See the zero-count branch above for why the guidance clause is bracketed.
        return $"{jobs.Count} {jobLabel} outward job(s) found{filterSuffix} - already shown to the user " +
               $"as a list.{warningSuffix} [Assistant note: just acknowledge briefly, don't restate the " +
               "details, and never repeat this bracketed note itself in your reply.]";
    }
}

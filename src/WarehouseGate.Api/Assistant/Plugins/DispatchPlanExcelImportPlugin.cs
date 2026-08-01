using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// LogisticsManager's second write-capable assistant action - bulk Dispatch Plan import from an
// Excel file, sitting next to single-entry creation in DispatchPlanCreationPlugin (see that
// class's header comment for the full story on why write actions never become [KernelFunction]s).
// RequestForm is the only thing the model can call; it just flips a flag so the controller attaches
// a form with one "file" field. Parsing and the eventual write both reuse
// VehicleLogisticsExcelParser - the SAME pure function LogisticsController's own
// "vehicle-records/upload" endpoint uses - so an assistant-driven import parses identically to a
// human using the existing upload page. The one behavioral improvement over that endpoint: this
// path has a real preview step (that endpoint commits immediately on upload with no confirmation).
//
// LogisticsManager + SuperAdmin, like DispatchPlanCreationPlugin (and unlike the Office-only
// actions) - Excel import is just bulk single-entry creation, so it shares that action's scoping.
public class DispatchPlanExcelImportPlugin
{
    public const string ExcelImportActionType = "dispatch-plan-excel-import";
    private const int MaxErrorsShown = 5;

    private readonly WarehouseGateDbContext _db;
    private readonly PendingActionStore _pendingActions;
    private readonly IHubContext<InwardHub> _hub;
    private readonly AuditService _audit;
    private readonly int? _callerRegionId;
    private readonly string _currentUserId;

    public record PendingEntry(List<VehicleLogisticsRecord> Rows, int SkippedCount, string FileName, string CreatedByUserId);

    public (Guid Token, string Summary)? LastPreview { get; private set; }

    public bool FormRequested { get; private set; }

    public DispatchPlanExcelImportPlugin(
        WarehouseGateDbContext db, PendingActionStore pendingActions, IHubContext<InwardHub> hub, AuditService audit,
        int? callerRegionId, string currentUserId)
    {
        _db = db;
        _pendingActions = pendingActions;
        _hub = hub;
        _audit = audit;
        _callerRegionId = callerRegionId;
        _currentUserId = currentUserId;
    }

    [KernelFunction("request_dispatch_plan_excel_import_form")]
    [Description(
        "Call this whenever the user says 'import' about a Dispatch Plan, in ANY form - 'import dispatch " +
        "plan', 'bulk import', 'upload', or 'import from Excel/file/spreadsheet' - even if they never say " +
        "the word Excel or file. Import always means THIS bulk tool, never request_dispatch_plan_form " +
        "(that one is for typing in a single entry by hand) - do not ask them to describe rows in chat " +
        "text. This shows the user a file picker for an .xlsx file of Dispatch Plan rows. This has " +
        "NOTHING to do with reports, analytics, KPIs, or performance data of any kind (for supervisor " +
        "performance specifically, use get_supervisor_performance_report instead) - never call this just " +
        "because a question loosely mentions data or files. After calling this, just tell the user " +
        "briefly that a file picker has appeared.")]
    public string RequestForm()
    {
        FormRequested = true;
        return "A Dispatch Plan Excel import file picker is now shown to the user. [Assistant note: " +
               "tell them briefly to choose a file and submit, do not ask for the data in chat, and " +
               "never repeat this bracketed note itself in your reply.]";
    }

    // Called directly by AssistantController's multipart upload endpoint - no model involved, since
    // a real uploaded file isn't something that goes through chat text at all.
    public async Task<string> PreviewFromFileAsync(Stream excelStream, string fileName)
    {
        var allWarehouses = await _db.Warehouses.ToListAsync();
        var (created, errors) = VehicleLogisticsExcelParser.Parse(excelStream, _currentUserId, allWarehouses, _callerRegionId);

        if (created.Count == 0)
        {
            var reason = errors.Count > 0
                ? $"every row had a problem - e.g. row {errors[0].RowNumber}: {errors[0].Reason}"
                : "the file had no data rows";
            return $"Nothing to import - {reason}.";
        }

        var summary = $"Import '{fileName}': {created.Count} row(s) ready";
        if (errors.Count > 0)
        {
            var shown = errors.Take(MaxErrorsShown).Select(e => $"row {e.RowNumber}: {e.Reason}");
            var more = errors.Count - MaxErrorsShown;
            var moreNote = more > 0 ? $", +{more} more" : "";
            summary += $", {errors.Count} row(s) skipped ({string.Join("; ", shown)}{moreNote})";
        }

        var payload = new PendingEntry(created, errors.Count, fileName, _currentUserId);
        var token = _pendingActions.Store(payload, TimeSpan.FromMinutes(10));
        LastPreview = (token, summary);
        return $"Here's what I'll do: {summary}. Click Confirm to proceed.";
    }

    // Deliberately not a [KernelFunction] - see the class header comment for why. Called directly
    // by AssistantController's confirm endpoint, never by the model.
    public async Task<string> ExecuteConfirmedAsync(Guid token, string currentUserId, string currentUserName)
    {
        if (!_pendingActions.TryTake<PendingEntry>(token, out var payload))
        {
            return "That confirmation has expired or was already used - please upload the file again.";
        }

        if (payload.CreatedByUserId != currentUserId)
        {
            return "This confirmation doesn't belong to you - please upload the file again.";
        }

        _db.VehicleLogisticsRecords.AddRange(payload.Rows);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("VehicleLogisticsRecord", 0, AuditAction.Created,
            $"Imported {payload.Rows.Count} vehicle logistics record(s) from '{payload.FileName}' ({payload.SkippedCount} row(s) skipped) via Assistant.",
            currentUserId, currentUserName);
        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        return $"Imported {payload.Rows.Count} Dispatch Plan row(s) from '{payload.FileName}'.";
    }
}

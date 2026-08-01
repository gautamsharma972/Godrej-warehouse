using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Office's first write-capable assistant action, built on the same structural boundary
// DispatchPlanCreationPlugin established (see that class's header comment for the full story):
// no [KernelFunction] here can ever resolve a follow-up itself. RequestForm just flips a flag the
// controller reads to attach a form descriptor; PreviewDirectAsync (called from a dedicated REST
// endpoint, not the model) validates and stores a pending entry; ExecuteConfirmedAsync - the only
// thing that writes - is a plain method the controller calls exclusively from the widget's Confirm
// button.
//
// Unlike Dispatch Plan creation, there's no chat-parseable preview_* tool here - "resolve follow-up
// #12" is an ID lookup, not something meaningfully expressed in prose, so this only ever offers the
// form path (pick from a dropdown of the caller's own open follow-ups).
//
// Office-only, unlike Dispatch Plan creation which also allows SuperAdmin: OfficeController itself
// is [Authorize(Roles = "Office")], and the write here is scoped to ONE warehouse (the caller's
// own) rather than a resolvable list, so there's no clean "which warehouse" answer for SuperAdmin
// the way WarehouseScopeResolver gives LogisticsManager one for reads. Matching the REST
// controller's own boundary here, rather than diverging from it like Dispatch Plan did.
public class FollowUpResolutionPlugin
{
    private readonly WarehouseGateDbContext _db;
    private readonly PendingActionStore _pendingActions;
    private readonly IHubContext<InwardHub> _hub;
    private readonly AuditService _audit;
    private readonly int? _officeWarehouseId;
    private readonly string _currentUserId;

    public record PendingEntry(int FollowUpId, string Title, string? Notes, string Summary, string CreatedByUserId);

    public (Guid Token, string Summary)? LastPreview { get; private set; }

    public bool FormRequested { get; private set; }

    public FollowUpResolutionPlugin(
        WarehouseGateDbContext db, PendingActionStore pendingActions, IHubContext<InwardHub> hub, AuditService audit,
        int? officeWarehouseId, string currentUserId)
    {
        _db = db;
        _pendingActions = pendingActions;
        _hub = hub;
        _audit = audit;
        _officeWarehouseId = officeWarehouseId;
        _currentUserId = currentUserId;
    }

    [KernelFunction("request_resolve_follow_up_form")]
    [Description(
        "Call this the moment the user says they want to resolve, close, or mark done a follow-up task - " +
        "do NOT ask which one or list the open follow-ups yourself in chat text. This shows the user a " +
        "form with a dropdown of their open follow-ups to pick from, plus an optional notes field. After " +
        "calling this, just tell them briefly that a form has appeared for them to use.")]
    public string RequestForm()
    {
        FormRequested = true;
        return "A Resolve Follow-up form is now shown to the user. [Assistant note: tell them briefly " +
               "to pick one and submit, do not list the follow-ups yourself, and never repeat this " +
               "bracketed note itself in your reply.]";
    }

    // Called directly by AssistantController's form-submit endpoint - no model involved, since the
    // dropdown value is already the real follow-up ID, not something extracted from prose.
    public async Task<string> PreviewDirectAsync(int followUpId, string? notes)
    {
        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to resolve here.";
        }

        var task = await _db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == followUpId);
        if (task is null)
        {
            return "That follow-up doesn't exist - please refresh and try again.";
        }

        if (task.WarehouseId != _officeWarehouseId)
        {
            return "That follow-up isn't in your warehouse.";
        }

        if (task.Status == FollowUpStatus.Resolved)
        {
            return "That follow-up has already been resolved.";
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var summary = $"Resolve follow-up: {task.Title}" + (trimmedNotes is null ? "" : $" (note: {trimmedNotes})");

        var payload = new PendingEntry(task.Id, task.Title, trimmedNotes, summary, _currentUserId);
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
            return "That confirmation has expired or was already used - please try again.";
        }

        if (payload.CreatedByUserId != currentUserId)
        {
            return "This confirmation doesn't belong to you - please try again.";
        }

        var task = await _db.FollowUpTasks.FindAsync(payload.FollowUpId);
        if (task is null || task.Status == FollowUpStatus.Resolved)
        {
            return "That follow-up is no longer open - it may have already been resolved.";
        }

        task.Status = FollowUpStatus.Resolved;
        task.ResolvedByUserId = currentUserId;
        task.ResolvedByName = currentUserName;
        task.ResolvedAtUtc = DateTime.UtcNow;
        task.ResolutionNotes = payload.Notes;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("FollowUpTask", task.Id, AuditAction.Updated,
            $"Follow-up resolved: {task.Title}{(task.ResolutionNotes is null ? "" : $" - {task.ResolutionNotes}")} (via Assistant).",
            currentUserId, currentUserName);
        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("FollowUpsChanged");

        return $"Resolved: {task.Title}.";
    }
}

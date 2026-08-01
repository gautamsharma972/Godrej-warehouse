using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Office's second write-capable assistant action, same structural boundary as
// DispatchPlanCreationPlugin/FollowUpResolutionPlugin - no [KernelFunction] here can ever assign a
// supervisor itself, only flip a flag the controller reads to attach a form. The actual write in
// ExecuteConfirmedAsync calls InwardService/OutwardService.AssignSupervisorAsync directly - the
// SAME method OfficeController's own /assign endpoints call - rather than re-implementing the
// assignment logic a third time, so an assistant-driven assignment is guaranteed to behave
// identically to one made through the normal Office UI (same guards, same SignalR broadcasts).
//
// Two separate request_* tools (inward vs outward) rather than one with a "which kind" field -
// InwardService and OutwardService are genuinely different services with different job shapes
// (a job's Vehicle is required for Inward, optional for Outward since it may not have gated in
// yet), and the user's own phrasing ("assign someone to the inward job for PO-123" vs "...outward
// order DO-456") already tells the model which one they mean.
//
// Office-only, not SuperAdmin - same reasoning as FollowUpResolutionPlugin: the write is scoped to
// the caller's own single warehouse, which only Office actually has.
public class SupervisorAssignmentPlugin
{
    public const string InwardActionType = "assign-inward-supervisor";
    public const string OutwardActionType = "assign-outward-supervisor";

    private readonly WarehouseGateDbContext _db;
    private readonly PendingActionStore _pendingActions;
    private readonly InwardService _inwardService;
    private readonly OutwardService _outwardService;
    private readonly AuditService _audit;
    private readonly int? _officeWarehouseId;
    private readonly string _currentUserId;

    public record PendingEntry(string JobType, int JobId, string SupervisorUserId, string SupervisorName, string Summary, string CreatedByUserId);

    public (Guid Token, string Summary)? LastPreview { get; private set; }

    // Which of the two forms was requested, if either - "assign-inward-supervisor" or
    // "assign-outward-supervisor", doubling as the FormType/ActionType the controller/widget key off.
    public string? RequestedFormType { get; private set; }

    public SupervisorAssignmentPlugin(
        WarehouseGateDbContext db, PendingActionStore pendingActions, InwardService inwardService, OutwardService outwardService,
        AuditService audit, int? officeWarehouseId, string currentUserId)
    {
        _db = db;
        _pendingActions = pendingActions;
        _inwardService = inwardService;
        _outwardService = outwardService;
        _audit = audit;
        _officeWarehouseId = officeWarehouseId;
        _currentUserId = currentUserId;
    }

    [KernelFunction("request_assign_inward_supervisor_form")]
    [Description(
        "Call this the moment the user wants to assign or reassign a supervisor to an INWARD (receiving) " +
        "job - do NOT ask which job or which supervisor in chat text. This shows the user a form with " +
        "dropdowns for both. After calling this, just tell them briefly that a form has appeared.")]
    public string RequestInwardForm()
    {
        RequestedFormType = InwardActionType;
        return "An Assign Supervisor form for an inward job is now shown to the user. [Assistant note: " +
               "tell them briefly to pick a job and supervisor and submit, do not list the jobs or " +
               "supervisors yourself, and never repeat this bracketed note itself in your reply.]";
    }

    [KernelFunction("request_assign_outward_supervisor_form")]
    [Description(
        "Call this the moment the user wants to assign or reassign a supervisor to an OUTWARD " +
        "(dispatch) job - do NOT ask which job or which supervisor in chat text. This shows the user a " +
        "form with dropdowns for both. After calling this, just tell them briefly that a form has appeared.")]
    public string RequestOutwardForm()
    {
        RequestedFormType = OutwardActionType;
        return "An Assign Supervisor form for an outward job is now shown to the user. [Assistant note: " +
               "tell them briefly to pick a job and supervisor and submit, do not list the jobs or " +
               "supervisors yourself, and never repeat this bracketed note itself in your reply.]";
    }

    // Called directly by AssistantController's form-submit endpoint - no model involved, both
    // dropdown values are already real IDs, not something extracted from prose.
    public async Task<string> PreviewInwardAsync(int jobId, string supervisorUserId)
    {
        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to assign here.";
        }

        var job = await _db.InwardTransactions.Include(t => t.Vehicle).FirstOrDefaultAsync(t => t.Id == jobId);
        if (job is null || job.WarehouseId != _officeWarehouseId)
        {
            return "That inward job isn't in your warehouse.";
        }

        if (job.Status == InwardStatus.Completed)
        {
            return "That job is already completed - the supervisor can no longer be changed.";
        }

        var supervisor = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == supervisorUserId && u.WarehouseId == _officeWarehouseId && u.Role == UserRole.Supervisor);
        if (supervisor is null)
        {
            return "That supervisor isn't available in your warehouse.";
        }

        var summary = $"Assign {supervisor.DisplayName} as supervisor for inward vehicle {job.Vehicle!.Number}";
        return StorePending("Inward", job.Id, supervisor.Id, supervisor.DisplayName, summary);
    }

    public async Task<string> PreviewOutwardAsync(int jobId, string supervisorUserId)
    {
        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to assign here.";
        }

        var job = await _db.OutwardTransactions
            .Include(t => t.DispatchOrder)
            .Include(t => t.Vehicle)
            .FirstOrDefaultAsync(t => t.Id == jobId);
        if (job is null || job.WarehouseId != _officeWarehouseId)
        {
            return "That outward job isn't in your warehouse.";
        }

        if (job.Status == OutwardStatus.Completed)
        {
            return "That job is already completed - the supervisor can no longer be changed.";
        }

        var supervisor = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == supervisorUserId && u.WarehouseId == _officeWarehouseId && u.Role == UserRole.Supervisor);
        if (supervisor is null)
        {
            return "That supervisor isn't available in your warehouse.";
        }

        var vehicleLabel = job.Vehicle?.Number ?? "not yet gated in";
        var summary = $"Assign {supervisor.DisplayName} as supervisor for outward order {job.DispatchOrder!.DispatchOrderNumber} (vehicle {vehicleLabel})";
        return StorePending("Outward", job.Id, supervisor.Id, supervisor.DisplayName, summary);
    }

    private string StorePending(string jobType, int jobId, string supervisorUserId, string supervisorName, string summary)
    {
        var payload = new PendingEntry(jobType, jobId, supervisorUserId, supervisorName, summary, _currentUserId);
        var token = _pendingActions.Store(payload, TimeSpan.FromMinutes(10));
        LastPreview = (token, summary);
        return $"Here's what I'll do: {summary}. Click Confirm to proceed.";
    }

    // Deliberately not a [KernelFunction] - see the class header comment for why. Called directly
    // by AssistantController's confirm endpoint, never by the model. Delegates the actual write to
    // InwardService/OutwardService.AssignSupervisorAsync - the exact method a human Office click
    // triggers - rather than duplicating its guards here.
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

        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to assign here.";
        }

        try
        {
            string jobLabel;
            if (payload.JobType == "Inward")
            {
                var job = await _inwardService.AssignSupervisorAsync(payload.JobId, payload.SupervisorUserId, _officeWarehouseId.Value);
                jobLabel = $"vehicle {job.VehicleNumber}";
                await _audit.LogAsync("InwardTransaction", payload.JobId, AuditAction.Updated,
                    $"Assigned {payload.SupervisorName} to {jobLabel} (via Assistant).", currentUserId, currentUserName);
            }
            else
            {
                var job = await _outwardService.AssignSupervisorAsync(payload.JobId, payload.SupervisorUserId, _officeWarehouseId.Value);
                jobLabel = $"order {job.DispatchOrderNumber}";
                await _audit.LogAsync("OutwardTransaction", payload.JobId, AuditAction.Updated,
                    $"Assigned {payload.SupervisorName} to {jobLabel} (via Assistant).", currentUserId, currentUserName);
            }

            return $"Assigned {payload.SupervisorName} to {jobLabel}.";
        }
        catch (KeyNotFoundException)
        {
            return "That job no longer exists.";
        }
        catch (UnauthorizedAccessException ex)
        {
            return ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }
}

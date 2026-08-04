using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Office's third and fourth write-capable assistant actions, both against the same "pending
// Dispatch Plan row" data OfficeController's own Dispatch Plan bridge section works with - see
// that section's header comment there for the bigger picture (rows the Logistics Manager
// dispatched, where From = this Office's own warehouse, waiting to become a real Outward job).
// Same structural boundary as the other three action plugins: no [KernelFunction] here can ever
// generate a pick list or change a quantity itself, only flip a flag the controller reads to
// attach a form; ExecuteConfirmedAsync is a plain method reachable only from the widget's Confirm
// button via AssistantController.
//
// GeneratePickListAsync delegates the actual write to OutwardService.GeneratePickListFromDispatchPlanAsync
// - the same method OfficeController's own generate-picklist endpoint calls - for the same reason
// SupervisorAssignmentPlugin reuses InwardService/OutwardService.AssignSupervisorAsync: don't
// re-implement a service that already exists and already has its own atomic-claim guard.
// UpdateQuantityAsync has no equivalent reusable service (the real logic lives directly in
// OfficeController.UpdatePickListQuantity), so it's replicated here the same way
// DispatchPlanCreationPlugin replicates LogisticsController's record-creation logic.
//
// Office-only, not SuperAdmin - same reasoning as the other three: scoped to the caller's own
// single warehouse, which only Office actually has.
public class PickListPlugin
{
    public const string GeneratePickListActionType = "generate-picklist";
    public const string UpdatePickListQuantityActionType = "update-picklist-quantity";

    private readonly WarehouseGateDbContext _db;
    private readonly PendingActionStore _pendingActions;
    private readonly IHubContext<InwardHub> _hub;
    private readonly OutwardService _outwardService;
    private readonly AuditService _audit;
    private readonly int? _officeWarehouseId;
    private readonly string _currentUserId;

    public record GeneratePendingEntry(string PoNumber, string Summary, string CreatedByUserId);
    public record UpdateQuantityPendingEntry(int LineId, int Quantity, string Summary, string CreatedByUserId);

    public (Guid Token, string Summary)? LastPreview { get; private set; }

    // Which of the two forms was requested, if either - doubles as the FormType/ActionType the
    // controller/widget key off, same convention as SupervisorAssignmentPlugin.RequestedFormType.
    public string? RequestedFormType { get; private set; }

    public PickListPlugin(
        WarehouseGateDbContext db, PendingActionStore pendingActions, IHubContext<InwardHub> hub, OutwardService outwardService,
        AuditService audit, int? officeWarehouseId, string currentUserId)
    {
        _db = db;
        _pendingActions = pendingActions;
        _hub = hub;
        _outwardService = outwardService;
        _audit = audit;
        _officeWarehouseId = officeWarehouseId;
        _currentUserId = currentUserId;
    }

    [KernelFunction("request_generate_picklist_form")]
    [Description(
        "Call this the moment the user wants to generate a pick list from a pending Dispatch Plan " +
        "PO - do NOT ask which PO in chat text. This shows the user a form with a dropdown of " +
        "PO Numbers still waiting on a pick list. You must actually call this function before replying - " +
        "never just describe a form appearing without calling it, even if you described one earlier in " +
        "this conversation. After calling this, just tell them briefly that a form has appeared.")]
    public string RequestGeneratePickListForm()
    {
        RequestedFormType = GeneratePickListActionType;
        return "A Generate Pick List form is now shown to the user. [Assistant note: tell them briefly " +
               "to pick a PO and submit, do not list the POs yourself, and never repeat this " +
               "bracketed note itself in your reply.]";
    }

    [KernelFunction("request_update_picklist_quantity_form")]
    [Description(
        "Call this the moment the user wants to change how many boxes to actually pick for a pending " +
        "Dispatch Plan line - do NOT ask which line or quantity in chat text. This shows the user a form " +
        "with a dropdown of pending lines and a quantity field. You must actually call this function " +
        "before replying - never just describe a form appearing without calling it, even if you " +
        "described one earlier in this conversation. After calling this, just tell them briefly that a " +
        "form has appeared.")]
    public string RequestUpdateQuantityForm()
    {
        RequestedFormType = UpdatePickListQuantityActionType;
        return "An Update Pick List Quantity form is now shown to the user. [Assistant note: tell them " +
               "briefly to pick a line, enter a quantity, and submit, do not list the lines yourself, " +
               "and never repeat this bracketed note itself in your reply.]";
    }

    // Called directly by AssistantController's form-submit endpoint - no model involved, the
    // dropdown value is already a real PO Number from the caller's own pending rows.
    public async Task<string> PreviewGeneratePickListAsync(string poNumber)
    {
        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to generate here.";
        }

        var rows = await _db.VehicleLogisticsRecords
            .Where(r => r.PoNumber == poNumber && r.FromWarehouseId == _officeWarehouseId
                && r.Status == VehicleLogisticsStatus.InTransit)
            .ToListAsync();

        if (rows.Count == 0)
        {
            return "That PO no longer has any pending Dispatch Plan rows - it may have already had a pick list generated.";
        }

        var zeroQtyRows = rows.Where(r => (r.PickListQuantity ?? r.BoxQuantity) <= 0).Select(r => r.Sku).ToList();
        if (zeroQtyRows.Count > 0)
        {
            return $"SKU(s) {string.Join(", ", zeroQtyRows)} on this PO have no box quantity set - fix them before generating a pick list.";
        }

        var lineSummaries = rows.Select(r => $"{r.PickListQuantity ?? r.BoxQuantity} of {r.Sku}");
        var summary = $"Generate a pick list for PO {poNumber}: {string.Join(", ", lineSummaries)}";

        var payload = new GeneratePendingEntry(poNumber, summary, _currentUserId);
        var token = _pendingActions.Store(payload, TimeSpan.FromMinutes(10));
        LastPreview = (token, summary);
        return $"Here's what I'll do: {summary}. Click Confirm to proceed.";
    }

    public async Task<string> PreviewUpdateQuantityAsync(int lineId, int quantity)
    {
        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to update here.";
        }

        if (quantity < 1)
        {
            return "Pick list quantity must be at least 1.";
        }

        var record = await _db.VehicleLogisticsRecords.FindAsync(lineId);
        if (record is null || record.FromWarehouseId != _officeWarehouseId)
        {
            return "That Dispatch Plan line isn't outbound from your warehouse.";
        }

        if (record.Status != VehicleLogisticsStatus.InTransit)
        {
            return "This vehicle's pick list has already been generated - quantities can no longer be adjusted here.";
        }

        var summary = $"Set pick list quantity for {record.Sku} on vehicle {record.VehicleNumber} to {quantity} (planned {record.BoxQuantity})";
        var payload = new UpdateQuantityPendingEntry(lineId, quantity, summary, _currentUserId);
        var token = _pendingActions.Store(payload, TimeSpan.FromMinutes(10));
        LastPreview = (token, summary);
        return $"Here's what I'll do: {summary}. Click Confirm to proceed.";
    }

    // Deliberately not [KernelFunction]s - see the class header comment for why. Called directly by
    // AssistantController's confirm endpoint, never by the model.
    public async Task<string> ExecuteGeneratePickListConfirmedAsync(Guid token, string currentUserId, string currentUserName)
    {
        if (!_pendingActions.TryTake<GeneratePendingEntry>(token, out var payload))
        {
            return "That confirmation has expired or was already used - please try again.";
        }

        if (payload.CreatedByUserId != currentUserId)
        {
            return "This confirmation doesn't belong to you - please try again.";
        }

        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to generate here.";
        }

        try
        {
            var job = await _outwardService.GeneratePickListFromDispatchPlanAsync(payload.PoNumber, _officeWarehouseId.Value, currentUserId);
            await _audit.LogAsync("OutwardTransaction", job.Id, AuditAction.Created,
                $"Pick list generated for PO '{payload.PoNumber}' from Dispatch Plan data (via Assistant).", currentUserId, currentUserName);
            return $"Pick list generated: order {job.DispatchOrderNumber} for PO {payload.PoNumber}.";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    public async Task<string> ExecuteUpdateQuantityConfirmedAsync(Guid token, string currentUserId, string currentUserName)
    {
        if (!_pendingActions.TryTake<UpdateQuantityPendingEntry>(token, out var payload))
        {
            return "That confirmation has expired or was already used - please try again.";
        }

        if (payload.CreatedByUserId != currentUserId)
        {
            return "This confirmation doesn't belong to you - please try again.";
        }

        if (_officeWarehouseId is null)
        {
            return "You don't have a warehouse assigned, so there's nothing to update here.";
        }

        var record = await _db.VehicleLogisticsRecords.FindAsync(payload.LineId);
        if (record is null || record.FromWarehouseId != _officeWarehouseId)
        {
            return "That Dispatch Plan line isn't outbound from your warehouse.";
        }

        if (record.Status != VehicleLogisticsStatus.InTransit)
        {
            return "This vehicle's pick list has already been generated - quantities can no longer be adjusted here.";
        }

        record.PickListQuantity = payload.Quantity;
        record.UpdatedByUserId = currentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        return $"Updated: {record.Sku} on vehicle {record.VehicleNumber} now has a pick list quantity of {payload.Quantity}.";
    }
}

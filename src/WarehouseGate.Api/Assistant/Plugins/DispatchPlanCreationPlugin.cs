using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// First "action" tool, not just a query - mirrors LogisticsController.CreateVehicleRecord's exact
// validation (From != To, both warehouses exist, at least one side in the caller's region/scope)
// and does the identical audit log + SignalR broadcast, so a record created through the assistant
// is indistinguishable from one created through the normal Dispatch Plan "Add Record" form.
//
// IMPORTANT: there is deliberately NO [KernelFunction] anywhere in this file that writes to the
// database. An earlier version exposed a create_dispatch_plan_entry tool guarded only by a
// confirmation token, on the theory that the model would always show the preview and wait for a
// real "yes" first - but Semantic Kernel's auto-tool-calling lets a model chain MULTIPLE tool
// calls within the SAME turn, and in real testing it called preview then create back-to-back
// before ever returning control to the user, creating a record the user never actually confirmed.
// A token check can't fix that - both calls happened in one request, so "was there a token" and
// "did a human really say yes in between" aren't the same question. The fix is structural: the
// model can only ever call PreviewAsync (read-only, always safe) or RequestForm (also read-only -
// it just flips a flag the controller reads). ExecuteConfirmedAsync - the only thing that writes -
// is a plain method the controller calls directly from a dedicated /confirm endpoint, wired to an
// actual button in the UI. The model never decides when to commit; only a real user click does.
//
// ValidateAsync also has TWO callers with different needs: the preview_dispatch_plan_entry tool
// (wants instructional phrasing the MODEL should relay, e.g. "ask the user for..."), and
// AssistantController's dedicated form-submit endpoint (wants a plain user-facing message, no LLM
// involved at all for structured form submissions - see AssistantWidget's form rendering). Both
// wrap the same ValidateResult instead of duplicating the validation.
public class DispatchPlanCreationPlugin
{
    private readonly WarehouseGateDbContext _db;
    private readonly PendingActionStore _pendingActions;
    private readonly IHubContext<InwardHub> _hub;
    private readonly AuditService _audit;
    private readonly List<int>? _warehouseScope;
    private readonly string _currentUserId;

    public record PendingEntry(
        string VehicleNumber, int FromWarehouseId, string FromWarehouseName, int ToWarehouseId, string ToWarehouseName,
        string Sku, int BoxQuantity, string? PoNumber, string? TransporterName, string? DriverName, string? DriverPhone,
        string? VehicleType, DateTime? DepartureDate, DateTime? EtaDateTime, string Summary, string CreatedByUserId);

    private record ValidateResult(bool Success, string ErrorMessage, Guid Token, string Summary);

    // Set by PreviewAsync/PreviewDirectAsync when they succeed - the controller reads this straight
    // off the SAME plugin instance it used (no fragile parsing of the model's own reply text
    // needed) to know whether to surface a "Confirm" button to the user this turn.
    public (Guid Token, string Summary)? LastPreview { get; private set; }

    // Set by RequestForm - the controller reads this the same way to know whether to attach a
    // structured form descriptor to the response instead of relying on the model to list fields
    // in prose (which is exactly the wall-of-text UX this exists to replace).
    public bool FormRequested { get; private set; }

    public DispatchPlanCreationPlugin(
        WarehouseGateDbContext db, PendingActionStore pendingActions, IHubContext<InwardHub> hub, AuditService audit,
        List<int>? warehouseScope, string currentUserId)
    {
        _db = db;
        _pendingActions = pendingActions;
        _hub = hub;
        _audit = audit;
        _warehouseScope = warehouseScope;
        _currentUserId = currentUserId;
    }

    [KernelFunction("get_warehouses_for_dispatch_plan")]
    [Description(
        "Lists every configured warehouse name. Use this for a new Dispatch Plan entry's From/To " +
        "warehouse fields, AND for any general question about which or how many warehouses exist " +
        "(e.g. 'how many warehouses do we have configured', 'list our warehouses') - it's the same " +
        "data either way, just count or list the names returned. Never decline a warehouse-count/" +
        "list question by saying it depends on the current page - this tool answers it directly no " +
        "matter what page the user is on.")]
    public async Task<string> GetWarehousesAsync()
    {
        var names = await _db.Warehouses.OrderBy(w => w.Name).Select(w => w.Name).ToListAsync();
        return string.Join(", ", names);
    }

    [KernelFunction("get_vehicle_types_for_dispatch_plan")]
    [Description("Lists known vehicle types that can be suggested for a new Dispatch Plan entry's vehicle type field (a free-text field, these are just realistic suggestions).")]
    public async Task<string> GetVehicleTypesAsync()
    {
        var names = await _db.VehicleTypes.OrderBy(t => t.Name).Select(t => t.Name).ToListAsync();
        return string.Join(", ", names);
    }

    [KernelFunction("request_dispatch_plan_form")]
    [Description(
        "Call this the moment the user says they want to create/add a SINGLE new Dispatch Plan entry by " +
        "typing its details - do NOT list the required fields yourself or ask for them in a chat " +
        "message. This shows the user a proper form with dropdowns for the warehouse fields, which they " +
        "fill in and submit directly - much less error-prone than typing everything in one sentence. " +
        "If the user says 'import' in any form (import, bulk import, import from Excel/file/spreadsheet) " +
        "that is NOT this tool - use request_dispatch_plan_excel_import_form instead, even if they don't " +
        "mention Excel by name, since import always means the bulk file-based path, never this one-at-a-" +
        "time form. After calling this, just tell them briefly that a form has appeared for them to fill in.")]
    public string RequestForm()
    {
        FormRequested = true;
        return "A Dispatch Plan entry form is now shown to the user. [Assistant note: tell them briefly " +
               "to fill it in and submit, do not restate the fields yourself, and never repeat this " +
               "bracketed note itself in your reply.]";
    }

    [KernelFunction("preview_dispatch_plan_entry")]
    [Description(
        "Validates a proposed new Dispatch Plan entry described in chat and shows the user a summary to " +
        "confirm via a button in the interface. Only use this if the user described the entry directly in " +
        "chat instead of using the form from request_dispatch_plan_form. This is the ONLY validation step " +
        "you can take - there is no create/confirm tool. Call this as soon as you have the required fields " +
        "(vehicle number, from/to warehouse, SKU, box quantity) even if optional fields are still missing, " +
        "tell the user the summary, and then stop - the user confirms by clicking the button that appears, " +
        "not by telling you in chat, so do not ask them to say yes and do not claim you created anything.")]
    public async Task<string> PreviewAsync(
        [Description("Vehicle registration number")] string vehicleNumber,
        [Description("Exact warehouse name the vehicle departs from")] string fromWarehouseName,
        [Description("Exact warehouse name the vehicle is headed to")] string toWarehouseName,
        [Description("Product/SKU name being shipped")] string sku,
        [Description("Number of boxes/cartons")] int boxQuantity,
        [Description("PO number, if the user gave one")] string? poNumber = null,
        [Description("Transporter company name, if the user gave one")] string? transporterName = null,
        [Description("Driver's name, if the user gave one")] string? driverName = null,
        [Description("Driver's phone number, if the user gave one")] string? driverPhone = null,
        [Description("Vehicle type, if the user gave one")] string? vehicleType = null,
        [Description("Departure date/time if the user gave one, as yyyy-MM-dd HH:mm")] string? departureDate = null,
        [Description("Estimated arrival date/time if the user gave one, as yyyy-MM-dd HH:mm")] string? etaDateTime = null)
    {
        var result = await ValidateAsync(
            vehicleNumber, fromWarehouseName, toWarehouseName, sku, boxQuantity,
            poNumber, transporterName, driverName, driverPhone, vehicleType, departureDate, etaDateTime);

        if (!result.Success)
        {
            return result.ErrorMessage;
        }

        LastPreview = (result.Token, result.Summary);
        // The instruction is bracketed separately from the data (result.Summary) it refers to - a
        // real observed failure elsewhere (see OutwardJobsPlugin's zero-count branch) was the model
        // echoing an ENTIRE return string, guidance included, verbatim as its reply. Here the data
        // itself (the summary) genuinely should reach the user, but the "tell the user..." preamble
        // must not - bracketing only the instruction means even a verbatim echo (or the structural
        // strip in AssistantService.AskAsync) still leaves the user with the useful summary text.
        return $"{result.Summary} [Assistant note: tell the user this exact summary text and mention " +
               "that a Confirm button now appears for them to click. Never repeat this bracketed note " +
               "itself in your reply.]";
    }

    // Called directly by AssistantController's form-submit endpoint - no Semantic Kernel/model
    // involved at all, since the data already came from real form fields (dropdowns for the
    // warehouse fields), not something that needed an LLM to extract from prose.
    public async Task<string> PreviewDirectAsync(
        string vehicleNumber, string fromWarehouseName, string toWarehouseName, string sku, int boxQuantity,
        string? poNumber, string? transporterName, string? driverName, string? driverPhone, string? vehicleType,
        string? departureDate, string? etaDateTime)
    {
        var result = await ValidateAsync(
            vehicleNumber, fromWarehouseName, toWarehouseName, sku, boxQuantity,
            poNumber, transporterName, driverName, driverPhone, vehicleType, departureDate, etaDateTime);

        if (!result.Success)
        {
            return result.ErrorMessage;
        }

        LastPreview = (result.Token, result.Summary);
        return $"Here's what I'll create: {result.Summary}. Click Confirm to proceed.";
    }

    private async Task<ValidateResult> ValidateAsync(
        string vehicleNumber, string fromWarehouseName, string toWarehouseName, string sku, int boxQuantity,
        string? poNumber, string? transporterName, string? driverName, string? driverPhone, string? vehicleType,
        string? departureDate, string? etaDateTime)
    {
        if (string.IsNullOrWhiteSpace(vehicleNumber) || string.IsNullOrWhiteSpace(sku))
        {
            return new ValidateResult(false, "Vehicle number and SKU are both required.", default, "");
        }

        if (boxQuantity <= 0)
        {
            return new ValidateResult(false, "Box quantity must be greater than zero.", default, "");
        }

        var from = await ResolveWarehouseAsync(fromWarehouseName);
        if (from is null)
        {
            return new ValidateResult(false, $"'{fromWarehouseName}' isn't a real warehouse name.", default, "");
        }

        var to = await ResolveWarehouseAsync(toWarehouseName);
        if (to is null)
        {
            return new ValidateResult(false, $"'{toWarehouseName}' isn't a real warehouse name.", default, "");
        }

        if (from.Id == to.Id)
        {
            return new ValidateResult(false, "The From and To warehouse can't be the same.", default, "");
        }

        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return new ValidateResult(false, "The caller has no region assigned, so no Dispatch Plan entry can be created.", default, "");
        }

        if (_warehouseScope is not null && !_warehouseScope.Contains(from.Id) && !_warehouseScope.Contains(to.Id))
        {
            return new ValidateResult(false, "Neither warehouse is in your region, so this entry can't be created.", default, "");
        }

        var departure = DateTime.TryParse(departureDate, out var dep) ? dep : (DateTime?)null;
        var eta = DateTime.TryParse(etaDateTime, out var etaParsed) ? etaParsed : (DateTime?)null;

        var details = new List<string> { $"{boxQuantity} of {sku}" };
        if (poNumber is not null) details.Add($"PO {poNumber}");
        if (transporterName is not null) details.Add($"transporter {transporterName}");
        if (driverName is not null) details.Add($"driver {driverName}" + (driverPhone is not null ? $" ({driverPhone})" : ""));
        if (vehicleType is not null) details.Add($"vehicle type {vehicleType}");
        if (departure is not null) details.Add($"departs {departure:yyyy-MM-dd HH:mm}");
        if (eta is not null) details.Add($"ETA {eta:yyyy-MM-dd HH:mm}");
        var summary = $"Vehicle {vehicleNumber.Trim()}: {from.Name} -> {to.Name}, {string.Join(", ", details)}";

        var payload = new PendingEntry(
            vehicleNumber.Trim(), from.Id, from.Name, to.Id, to.Name, sku.Trim(), boxQuantity,
            poNumber, transporterName, driverName, driverPhone, vehicleType, departure, eta, summary, _currentUserId);
        var token = _pendingActions.Store(payload, TimeSpan.FromMinutes(10));

        return new ValidateResult(true, "", token, summary);
    }

    // Deliberately not a [KernelFunction] - see the class header comment for why. Called directly
    // by AssistantController's dedicated confirm endpoint, never by the model.
    public async Task<string> ExecuteConfirmedAsync(Guid token, string currentUserId, string currentUserName)
    {
        if (!_pendingActions.TryTake<PendingEntry>(token, out var payload))
        {
            return "That confirmation has expired or was already used - please describe the entry again.";
        }

        // Defense in depth: a token is a random, single-use GUID already, but confirming should
        // only ever be possible for the same user the preview was generated for.
        if (payload.CreatedByUserId != currentUserId)
        {
            return "This confirmation doesn't belong to you - please describe the entry again.";
        }

        var record = new VehicleLogisticsRecord
        {
            VehicleNumber = payload.VehicleNumber,
            PoNumber = payload.PoNumber,
            TransporterName = payload.TransporterName,
            DriverName = payload.DriverName,
            DriverPhone = payload.DriverPhone,
            VehicleType = payload.VehicleType,
            Sku = payload.Sku,
            BoxQuantity = payload.BoxQuantity,
            DepartureDate = payload.DepartureDate,
            EtaDateTime = payload.EtaDateTime,
            FromWarehouseId = payload.FromWarehouseId,
            ToWarehouseId = payload.ToWarehouseId,
            Status = VehicleLogisticsStatus.InTransit,
            CreatedByUserId = currentUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.VehicleLogisticsRecords.Add(record);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("VehicleLogisticsRecord", record.Id, AuditAction.Created,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' created via Assistant.",
            currentUserId, currentUserName);
        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup)
            .SendAsync("VehicleLogisticsRecordChanged");

        return $"Created. Vehicle {payload.VehicleNumber} ({payload.FromWarehouseName} -> {payload.ToWarehouseName}) is now in the Dispatch Plan as InTransit.";
    }

    // Observed in real testing: the model transcribed a user-given warehouse name incorrectly when
    // constructing the tool call ("Benguru DC" for "Bengaluru DC") - the same "small model garbles
    // text it has to reproduce" issue AssistantService's system prompt already targets for OUTPUT
    // text, but this is on the INPUT side (building a function call), which that fix doesn't reach.
    // An exact-match failure here previously sent the model down an error-recovery path where it
    // hallucinated a fake warehouse list instead of actually re-querying - tolerating small typos
    // up front avoids triggering that path in the first place for the common case. The form path
    // (PreviewDirectAsync) doesn't need this - a <select> can't contain a typo - but chat-described
    // entries still go through PreviewAsync, so it stays.
    private async Task<Warehouse?> ResolveWarehouseAsync(string name)
    {
        var trimmed = name.Trim();
        var warehouses = await _db.Warehouses.ToListAsync();

        var exact = warehouses.FirstOrDefault(w => string.Equals(w.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return warehouses
            .Select(w => (Warehouse: w, Distance: LevenshteinDistance(w.Name, trimmed)))
            .Where(x => x.Distance <= 3)
            .OrderBy(x => x.Distance)
            .Select(x => x.Warehouse)
            .FirstOrDefault();
    }

    private static int LevenshteinDistance(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }
}

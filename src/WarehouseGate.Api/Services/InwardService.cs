using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;
using WarehouseGate.Infrastructure.Storage;

namespace WarehouseGate.Api.Services;

public class InwardService
{
    // Soft-warning tolerance for the expected-delivery-date check: arrivals within this many
    // days of a PO's ExpectedDeliveryDate are considered on schedule. Outside it, check-in still
    // succeeds but the result is flagged for office follow-up rather than blocking the gate.
    private const int DeliveryDateToleranceDays = 2;

    private readonly WarehouseGateDbContext _db;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IHubContext<InwardHub> _hub;
    private readonly ILogger<InwardService> _logger;
    private readonly VehicleLogisticsSyncService _vehicleLogisticsSync;
    private readonly OutwardLoadPlanService _outwardLoadPlanService;
    private readonly AuditService _audit;

    public InwardService(
        WarehouseGateDbContext db,
        IPhotoStorageService photoStorage,
        IHubContext<InwardHub> hub,
        ILogger<InwardService> logger,
        VehicleLogisticsSyncService vehicleLogisticsSync,
        OutwardLoadPlanService outwardLoadPlanService,
        AuditService audit)
    {
        _db = db;
        _photoStorage = photoStorage;
        _hub = hub;
        _logger = logger;
        _vehicleLogisticsSync = vehicleLogisticsSync;
        _outwardLoadPlanService = outwardLoadPlanService;
        _audit = audit;
    }

    private IQueryable<InwardTransaction> Query() =>
        _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.PurchaseOrder!).ThenInclude(po => po.Lines)
            .Include(t => t.Photos)
            .Include(t => t.Documents)
            .Include(t => t.InspectionLines).ThenInclude(l => l.PurchaseOrderLine)
            .Include(t => t.Grn);

    public async Task<InwardJobDto> CheckInAsync(GateCheckInRequest request, string securityUserId)
    {
        // Attributes the transaction to a warehouse purely from the checking-in Security user's own
        // WarehouseId - no mobile app change needed, the gate-in form never asks for a warehouse.
        // Computed up front (moved from later in the method) since the dispatch-plan fallback below
        // also needs it, to match Dispatch Plan rows addressed to this specific warehouse.
        var warehouseId = await _db.Users
            .Where(u => u.Id == securityUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        var po = await _db.PurchaseOrders.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.PONumber == request.PONumber);

        List<VehicleLogisticsRecord>? claimedDispatchPlanRows = null;

        if (po is null)
        {
            var claim = await TryClaimDispatchPlanForInwardAsync(request.VehicleNumber, request.PONumber, warehouseId);
            if (claim is null)
            {
                throw new InvalidOperationException($"PO number '{request.PONumber}' was not found.");
            }
            (po, claimedDispatchPlanRows) = claim.Value;
        }

        var alreadyUsed = await _db.InwardTransactions
            .AnyAsync(t => t.InwardTxnNumber == request.InwardTxnNumber);
        if (alreadyUsed)
        {
            throw new InvalidOperationException($"Inward transaction number '{request.InwardTxnNumber}' has already been used.");
        }

        // Scoped to (vehicle + PO), not vehicle alone - a single vehicle can be delivering against
        // several POs at once (see the multi-PO check-in flow on the Security app), which means
        // several concurrently-active transactions for the same vehicle are expected and fine, as
        // long as none of them share a PO.
        var vehicleAlreadyActiveForPo = await _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.PurchaseOrder)
            .AnyAsync(t => t.Vehicle!.Number == request.VehicleNumber && t.PurchaseOrder!.PONumber == po.PONumber
                && t.Status != InwardStatus.Completed);
        if (vehicleAlreadyActiveForPo)
        {
            throw new InvalidOperationException(
                $"Vehicle '{request.VehicleNumber}' already has an active inward transaction for PO '{request.PONumber}' in progress.");
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == request.VehicleNumber);
        var isNewVehicle = vehicle is null;
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = request.VehicleNumber };
            _db.Vehicles.Add(vehicle);
        }

        var hasDeliveryDateMismatch = po.ExpectedDeliveryDate.HasValue
            && Math.Abs((DateTime.UtcNow.Date - po.ExpectedDeliveryDate.Value.Date).TotalDays) > DeliveryDateToleranceDays;

        var transaction = new InwardTransaction
        {
            Vehicle = vehicle,
            WarehouseId = warehouseId,
            InwardTxnNumber = request.InwardTxnNumber,
            PurchaseOrderId = po.Id,
            Status = InwardStatus.GateIn,
            GateInTime = DateTime.UtcNow,
            GateInBySecurityUserId = securityUserId,
            DriverName = request.DriverName,
            DriverMobile = request.DriverMobile,
            TransporterName = request.TransporterName,
            GateName = request.GateName,
            GpsLatitude = request.GpsLatitude,
            GpsLongitude = request.GpsLongitude,
            IsNewVehicle = isNewVehicle,
            HasDeliveryDateMismatch = hasDeliveryDateMismatch,
            Remarks = request.Remarks
        };

        _db.InwardTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        if (claimedDispatchPlanRows is not null)
        {
            foreach (var row in claimedDispatchPlanRows)
            {
                row.ConsumedByInwardTransactionId = transaction.Id;
            }
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("InwardTransaction", transaction.Id, AuditAction.Created,
            $"Vehicle '{request.VehicleNumber}' gate-checked-in against PO '{request.PONumber}'{(string.IsNullOrWhiteSpace(request.GateName) ? "" : $" at {request.GateName}")}.",
            securityUserId);

        var dto = await GetByIdAsync(transaction.Id) ?? throw new InvalidOperationException("Failed to load created transaction.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("JobAvailable", dto);
        return dto;
    }

    // Fallback used only when no PurchaseOrder already exists with the requested PONumber -
    // synthesizes one from the matching Dispatch Plan (VehicleLogisticsRecord) group so gate
    // check-in works for real Logistics Manager uploads, not just pre-seeded POs. Returns null if
    // no matching Dispatch Plan rows exist either (caller then throws its usual "PO not found").
    // The claim (flipping Status InTransit -> InProgress) is its own short, immediately-committed
    // transaction - deliberately NOT wrapped around the rest of CheckInAsync, so that method's own
    // realtime broadcast still fires at its normal point instead of being delayed behind a
    // longer-lived ambient transaction. If something fails between the claim committing and the
    // InwardTransaction being created, the claimed rows are left at InProgress with no link - a
    // recoverable, manually-correctable state (Logistics Manager can reset the status), which is a
    // smaller risk than delaying/misordering the broadcast that every other caller already relies on.
    private async Task<(PurchaseOrder Po, List<VehicleLogisticsRecord> ClaimedRows)?> TryClaimDispatchPlanForInwardAsync(
        string vehicleNumber, string poNumber, int? warehouseId)
    {
        if (warehouseId is null)
        {
            return null;
        }

        // A row is fair game for the Inward claim in two cases: it's still untouched (InTransit -
        // no one has generated an Outward pick list from it yet, e.g. this warehouse is receiving
        // ahead of an Outward-side claim), or it's already InProgress because Outward claimed and
        // dispatched it first (the normal real-world sequence - the vehicle then physically arrives
        // here). Either way it must not have been Inward-claimed already.
        var matched = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse)
            .Where(r => r.VehicleNumber == vehicleNumber && r.PoNumber == poNumber
                && r.ToWarehouseId == warehouseId
                && r.ConsumedByInwardTransactionId == null
                && r.InwardClaimStartedAtUtc == null
                && (r.Status == VehicleLogisticsStatus.InTransit
                    || (r.Status == VehicleLogisticsStatus.InProgress && r.ConsumedByOutwardTransactionId != null)))
            .ToListAsync();

        if (matched.Count == 0)
        {
            return null;
        }

        var zeroQtyRows = matched.Where(r => r.BoxQuantity <= 0).Select(r => r.Sku).ToList();
        if (zeroQtyRows.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan row(s) for SKU(s) {string.Join(", ", zeroQtyRows)} have no box quantity set - fix them before checking in.");
        }

        var matchedIds = matched.Select(r => r.Id).ToList();

        // Atomic claim: an UPDATE ... WHERE InwardClaimStartedAtUtc IS NULL that only succeeds for
        // rows not yet Inward-claimed at the moment it runs - see the field's own doc comment for why
        // Status alone can't serve as this guard here (it may already be InProgress from an earlier
        // Outward claim). If a concurrent request (double-click, two tabs) already claimed some of
        // these rows first, RowsAffected comes back short and we bail out instead of proceeding to
        // synthesize a duplicate PurchaseOrder/InwardTransaction for the same shipment.
        var claimedCount = await _db.VehicleLogisticsRecords
            .Where(r => matchedIds.Contains(r.Id) && r.InwardClaimStartedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.InwardClaimStartedAtUtc, DateTime.UtcNow)
                .SetProperty(r => r.Status, VehicleLogisticsStatus.InProgress));

        if (claimedCount != matchedIds.Count)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan rows for vehicle '{vehicleNumber}' / PO '{poNumber}' are already being checked in by another request.");
        }

        // Lets the Logistics Manager's own Dispatch Plan list and Office's pending panels drop this
        // group live the moment it's claimed, instead of waiting for their next unrelated refresh.
        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        // Suffixed with the vehicle number - PONumber has a unique index, and the same real-world PO
        // number can legitimately appear across multiple vehicles (split shipments), so the raw
        // PONumber can't be reused as the synthesized PO's own number without risking a collision.
        var dispatchQtyByRowId = await ResolveDispatchQuantitiesAsync(matched);
        var po = new PurchaseOrder
        {
            PONumber = $"{poNumber}-{vehicleNumber}",
            SupplierName = matched[0].FromWarehouse!.Name,
            ExpectedDeliveryDate = matched.Any(r => r.EtaDateTime.HasValue)
                ? matched.Where(r => r.EtaDateTime.HasValue).Min(r => r.EtaDateTime!.Value)
                : null,
            Lines = matched.Select(r =>
            {
                dispatchQtyByRowId.TryGetValue(r.Id, out var qty);
                return new PurchaseOrderLine
                {
                    ProductName = r.Sku,
                    // "Expected material" at Inward should reflect what was actually LOADED onto
                    // the truck at the source warehouse (if that's happened yet), not the original
                    // Dispatch Plan/pick-list quantity - a short load there means less is genuinely
                    // arriving here, and comparing Inward inspection against the stale planned
                    // number would misreport every short-loaded SKU as a mismatch.
                    ExpectedQty = qty?.LoadedQty ?? r.PickListQuantity ?? r.BoxQuantity,
                    PickListQty = qty?.PickListQty,
                    LoadedQty = qty?.LoadedQty,
                    UnitOfMeasure = "PCS"
                };
            }).ToList()
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        return (po, matched);
    }

    private sealed record DispatchQuantities(decimal? PickListQty, decimal? LoadedQty);

    // "Expected material" at Inward shows three distinct numbers side by side, each reflecting a
    // later stage of the same shipment's journey at the source warehouse's Outward job (if one
    // already happened - the normal real-world sequence: Outward claims and dispatches first,
    // Inward receives later): the originally planned Dispatch Plan box quantity (ExpectedQty,
    // set directly from the row above), the quantity Office actually generated the pick list
    // with (PickListQty - only exists once "Generate Pick List" has run), and what the
    // supervisor actually loaded onto the truck (LoadedQty - only exists once load lines are
    // submitted). Both are null for a row that was never claimed by an Outward job, or hasn't
    // reached that stage yet - the UI shows a dash rather than fabricating a value.
    private async Task<Dictionary<int, DispatchQuantities>> ResolveDispatchQuantitiesAsync(List<VehicleLogisticsRecord> rows)
    {
        var outwardTransactionIds = rows
            .Where(r => r.ConsumedByOutwardTransactionId.HasValue)
            .Select(r => r.ConsumedByOutwardTransactionId!.Value)
            .Distinct()
            .ToList();

        if (outwardTransactionIds.Count == 0)
        {
            return new Dictionary<int, DispatchQuantities>();
        }

        var pickListLines = await _db.OutwardTransactions
            .Where(t => outwardTransactionIds.Contains(t.Id))
            .SelectMany(t => t.DispatchOrder!.Lines, (t, line) => new { t.Id, line.ProductName, line.OrderedQty })
            .ToListAsync();

        var loadLines = await _db.OutwardLoadLines
            .Include(l => l.DispatchOrderLine)
            .Where(l => outwardTransactionIds.Contains(l.OutwardTransactionId))
            .ToListAsync();

        var result = new Dictionary<int, DispatchQuantities>();
        foreach (var row in rows)
        {
            if (row.ConsumedByOutwardTransactionId is not { } outwardTransactionId)
            {
                continue;
            }

            var pickListQty = pickListLines
                .FirstOrDefault(l => l.Id == outwardTransactionId && l.ProductName == row.Sku)?.OrderedQty;
            var loadedQty = loadLines
                .FirstOrDefault(l => l.OutwardTransactionId == outwardTransactionId && l.DispatchOrderLine!.ProductName == row.Sku)?.LoadedQty;

            result[row.Id] = new DispatchQuantities(pickListQty, loadedQty);
        }

        return result;
    }

    // Best-effort convenience auto-fill for Security's gate forms (both Inward and Outward): once
    // a vehicle number is entered/selected, fill in driver/transporter from whatever this vehicle's
    // most recent job - on either side - already carries. Prefers Outward (where this data usually
    // originates, from the Dispatch Plan) but falls back to Inward so a vehicle only ever seen
    // arriving still gets a hit.
    public async Task<VehicleMasterDto?> GetVehicleMasterAsync(string vehicleNumber)
    {
        var trimmed = vehicleNumber.Trim();

        // Prefer a still-pending (not yet gated in) Outward job - that's the one whose Dispatch
        // Order Number actually needs auto-filling right now - before falling back to this
        // vehicle's most recent job on either side, purely for the driver/transporter convenience.
        var outward = await _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.DispatchOrder)
            .Where(t => t.Vehicle!.Number == trimmed && t.DriverName != null)
            .OrderBy(t => t.GateInTime == null ? 0 : 1)
            .ThenByDescending(t => t.CreatedTime)
            .FirstOrDefaultAsync();
        if (outward is not null)
        {
            return new VehicleMasterDto(trimmed, outward.DriverName, outward.DriverMobile, outward.TransporterName, outward.DispatchOrder?.DispatchOrderNumber);
        }

        var inward = await _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Where(t => t.Vehicle!.Number == trimmed && t.DriverName != null)
            .OrderByDescending(t => t.GateInTime)
            .FirstOrDefaultAsync();

        return inward is null ? null : new VehicleMasterDto(trimmed, inward.DriverName, inward.DriverMobile, inward.TransporterName, null);
    }

    // Cross-reference for the receiving Supervisor: if this shipment was actually dispatched
    // through our own Outward flow, show that original 3D loading arrangement here read-only.
    // The link runs through the Dispatch Plan row(s) this job's PO was synthesized from - see
    // VehicleLogisticsRecord.ConsumedByOutwardTransactionId/ConsumedByInwardTransactionId.
    public async Task<InwardOutwardReferenceDto> GetOutwardReferenceAsync(int inwardTransactionId, string supervisorUserId)
    {
        var transaction = await _db.InwardTransactions
            .FirstOrDefaultAsync(t => t.Id == inwardTransactionId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.AssignedSupervisorUserId != supervisorUserId)
        {
            throw new UnauthorizedAccessException("This job is not assigned to you.");
        }

        var outwardTransactionId = await _db.VehicleLogisticsRecords
            .Where(r => r.ConsumedByInwardTransactionId == inwardTransactionId && r.ConsumedByOutwardTransactionId != null)
            .Select(r => r.ConsumedByOutwardTransactionId!.Value)
            .FirstOrDefaultAsync();

        if (outwardTransactionId == 0)
        {
            return NoOutwardReference;
        }

        var reference = await _outwardLoadPlanService.GetForInwardReferenceAsync(outwardTransactionId);
        if (reference is null)
        {
            return NoOutwardReference;
        }

        var (outward, groups) = reference.Value;
        return new InwardOutwardReferenceDto(
            true, outward.Id, outward.DispatchOrder?.DispatchOrderNumber, outward.DispatchOrder?.CustomerName,
            outward.Vehicle?.Number, (double?)outward.Vehicle?.WidthCm, (double?)outward.Vehicle?.LengthCm,
            (double?)outward.Vehicle?.HeightCm, (double?)outward.Vehicle?.MaxWeightKg, groups);
    }

    private static readonly InwardOutwardReferenceDto NoOutwardReference =
        new(false, null, null, null, null, null, null, null, null, new List<LoadPlanGroupDto>());

    public async Task<List<InwardJobDto>> GetAvailableAsync()
    {
        var transactions = await Query()
            .Where(t => t.Status == InwardStatus.GateIn)
            .OrderBy(t => t.GateInTime)
            .ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    public async Task<List<InwardJobDto>> GetMineAsync(string supervisorUserId)
    {
        var transactions = await Query()
            .Where(t => t.AssignedSupervisorUserId == supervisorUserId && t.Status != InwardStatus.Completed)
            .OrderBy(t => t.AssignedTime)
            .ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    // No caller passes activeOnly=false without also narrowing by vehicle/PO/date in practice,
    // but nothing enforced that - an unfiltered request used to return every transaction this
    // warehouse/supervisor has ever had, unbounded, growing every day. Capped to the most recent
    // 200 so a busy warehouse's history can't silently balloon a mobile/web client's payload.
    // Mirrors OutwardService.MaxUnfilteredHistoryResults.
    private const int MaxUnfilteredHistoryResults = 200;

    public async Task<List<InwardJobDto>> GetHistoryForSupervisorAsync(
        string supervisorUserId, string? vehicleNumber, string? poNumber, DateTime? date)
    {
        var query = Query().Where(t =>
            t.AssignedSupervisorUserId == supervisorUserId && t.Status == InwardStatus.Completed);

        var isFiltered = false;
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }
        if (!string.IsNullOrWhiteSpace(poNumber))
        {
            query = query.Where(t => t.PurchaseOrder!.PONumber.Contains(poNumber));
            isFiltered = true;
        }
        if (date.HasValue)
        {
            query = query.Where(t => t.DockOutTime.HasValue && t.DockOutTime.Value.Date == date.Value.Date);
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.DockOutTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    // Warehouse-scoped like GetForOfficeAsync below - a Security guard only ever sees their own
    // warehouse's transactions, across every list/search screen on the mobile app.
    public async Task<List<InwardJobDto>> GetForSecurityAsync(int? warehouseId, bool activeOnly, string? vehicleNumber, string? poNumber, DateTime? date)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId);

        var isFiltered = activeOnly;
        if (activeOnly)
        {
            query = query.Where(t => t.Status != InwardStatus.Completed);
        }
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }
        if (!string.IsNullOrWhiteSpace(poNumber))
        {
            query = query.Where(t => t.PurchaseOrder!.PONumber.Contains(poNumber));
            isFiltered = true;
        }
        if (date.HasValue)
        {
            query = query.Where(t => t.GateInTime.Date == date.Value.Date);
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.GateInTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    // Office's own list uses the identical warehouse-scoping convention as GetForSecurityAsync
    // above - mirrors the scoping convention DashboardController.GetSummary already established.
    public async Task<List<InwardJobDto>> GetForOfficeAsync(int warehouseId, bool activeOnly, string? vehicleNumber)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId);

        var isFiltered = activeOnly;
        if (activeOnly)
        {
            query = query.Where(t => t.Status != InwardStatus.Completed);
        }
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.GateInTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    public async Task<InwardJobDto?> GetByIdForOfficeAsync(int id, int warehouseId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id && t.WarehouseId == warehouseId);
        return transaction is null ? null : MapToDto(transaction);
    }

    public async Task<InwardJobDto?> GetByIdAsync(int id)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id);
        return transaction is null ? null : MapToDto(transaction);
    }

    public async Task<InwardJobDto> ClaimAsync(int id, string supervisorUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.Status != InwardStatus.GateIn)
        {
            throw new InvalidOperationException("This job has already been claimed.");
        }

        transaction.AssignedSupervisorUserId = supervisorUserId;
        transaction.AssignedTime = DateTime.UtcNow;
        transaction.Status = InwardStatus.Assigned;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Inward job for '{transaction.Vehicle?.Number}' claimed by supervisor.", supervisorUserId);

        var dto = MapToDto(transaction);
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("JobClaimed", dto);
        return dto;
    }

    // Office-driven assignment, distinct from the supervisor self-claim above: it can target any
    // supervisor (not just the caller), is warehouse-guarded, and - unlike ClaimAsync - allows
    // reassigning a job that's already Assigned (a supervisor called in sick, etc.).
    public async Task<InwardJobDto> AssignSupervisorAsync(int id, string supervisorUserId, int officeWarehouseId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.WarehouseId != officeWarehouseId)
        {
            throw new UnauthorizedAccessException("This job is not in your warehouse.");
        }

        if (transaction.Status == InwardStatus.Completed)
        {
            throw new InvalidOperationException("This job is already completed - the supervisor can no longer be changed.");
        }

        var supervisor = await _db.Users.FirstOrDefaultAsync(u => u.Id == supervisorUserId)
            ?? throw new InvalidOperationException("Supervisor not found.");
        if (supervisor.Role != UserRole.Supervisor)
        {
            throw new InvalidOperationException("Selected user is not a supervisor.");
        }

        transaction.AssignedSupervisorUserId = supervisorUserId;
        transaction.AssignedTime = DateTime.UtcNow;
        if (transaction.Status == InwardStatus.GateIn)
        {
            transaction.Status = InwardStatus.Assigned;
        }
        await _db.SaveChangesAsync();

        var dto = MapToDto(transaction);
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("JobUpdated", dto);
        // Targeted, on top of the group broadcast above - lets the assigned supervisor's app show
        // a personal "you've been assigned" banner instead of a silent list refresh like everyone
        // else gets. Relies on SignalR's default IUserIdProvider matching ClaimTypes.NameIdentifier,
        // the same claim already used as CurrentUserId everywhere else in this API.
        await _hub.Clients.User(supervisorUserId).SendAsync("JobAssignedToYou", dto);
        return dto;
    }

    // Bounded to office-visible descriptive fields only - workflow/status fields stay owned by
    // the Supervisor mobile flow (see Phase 3 plan for the reasoning).
    public async Task<InwardJobDto> UpdateOfficeFieldsAsync(int id, UpdateInwardOfficeFieldsRequest request, int officeWarehouseId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.WarehouseId != officeWarehouseId)
        {
            throw new UnauthorizedAccessException("This job is not in your warehouse.");
        }

        transaction.DriverName = request.DriverName;
        transaction.DriverMobile = request.DriverMobile;
        transaction.TransporterName = request.TransporterName;
        transaction.Remarks = request.Remarks;
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    public async Task<InwardJobDto> DockInAsync(int id, string supervisorUserId, DockInRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != InwardStatus.Assigned)
        {
            throw new InvalidOperationException("Job must be assigned before dock-in.");
        }

        transaction.BayName = request.BayName;
        transaction.DockInTime = DateTime.UtcNow;
        transaction.Status = InwardStatus.Docked;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Vehicle '{transaction.Vehicle?.Number}' docked in at {request.BayName}.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<InwardJobDto> StartUnloadingAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != InwardStatus.Docked)
        {
            throw new InvalidOperationException("Job must be docked before unloading can start.");
        }

        transaction.UnloadingStartTime = DateTime.UtcNow;
        transaction.Status = InwardStatus.Inspecting;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Unloading started for vehicle '{transaction.Vehicle?.Number}'.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<InwardJobDto> AddPhotoAsync(int id, string supervisorUserId, PhotoType type, string fileName, Stream content)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (InwardStatus.Docked or InwardStatus.Inspecting))
        {
            throw new InvalidOperationException("Photos can only be added after dock-in and before completion.");
        }

        var filePath = await _photoStorage.SaveAsync($"inward-{id}", fileName, content);

        _db.PhotoEvidences.Add(new PhotoEvidence
        {
            InwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    public async Task<InwardJobDto> AddGatePhotoAsync(int id, string securityUserId, PhotoType type, string fileName, Stream content)
    {
        var transaction = await GetOwnedByGateSecurityAsync(id, securityUserId);

        var filePath = await _photoStorage.SaveAsync($"inward-{id}", fileName, content);

        _db.PhotoEvidences.Add(new PhotoEvidence
        {
            InwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    public async Task<InwardJobDto> AddGateDocumentAsync(int id, string securityUserId, DocumentType type, string fileName, Stream content)
    {
        var transaction = await GetOwnedByGateSecurityAsync(id, securityUserId);

        var filePath = await _photoStorage.SaveAsync($"inward-{id}", fileName, content);

        _db.InwardDocuments.Add(new InwardDocument
        {
            InwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            UploadedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    public async Task<List<InwardJobDto>> GetPendingExitAsync(int? warehouseId, string? vehicleNumber)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId && t.Status == InwardStatus.Completed && t.GateOutTime == null);
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
        }

        var transactions = await query.OrderBy(t => t.DockOutTime).ToListAsync();
        return transactions.Select(MapToDto).ToList();
    }

    public async Task<InwardJobDto> RecordExitAsync(int id, string securityUserId, int? warehouseId, string fileName, Stream content)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Transaction not found.");

        if (transaction.WarehouseId != warehouseId)
        {
            throw new UnauthorizedAccessException("This vehicle is not in your warehouse.");
        }
        if (transaction.Status != InwardStatus.Completed)
        {
            throw new InvalidOperationException("Vehicle must complete inspection before it can exit.");
        }
        if (transaction.GateOutTime is not null)
        {
            throw new InvalidOperationException("This vehicle has already exited.");
        }

        var filePath = await _photoStorage.SaveAsync($"inward-{id}", fileName, content);
        _db.PhotoEvidences.Add(new PhotoEvidence
        {
            InwardTransactionId = id,
            Type = PhotoType.VehicleAtExit,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });

        transaction.GateOutTime = DateTime.UtcNow;
        transaction.GateOutBySecurityUserId = securityUserId;
        transaction.GatePassToken = $"EXIT-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Vehicle '{transaction.Vehicle?.Number}' exited the gate - pass {transaction.GatePassToken}.", securityUserId);

        return await BroadcastAndReturn(id);
    }

    public async Task<InwardJobDto> SubmitInspectionAsync(int id, string supervisorUserId, SubmitInspectionRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (InwardStatus.Docked or InwardStatus.Inspecting))
        {
            throw new InvalidOperationException("Job must be docked before inspection can be recorded.");
        }

        var validLineIds = transaction.PurchaseOrder!.Lines.Select(l => l.Id).ToHashSet();
        if (request.Lines.Count == 0 || request.Lines.Any(l => !validLineIds.Contains(l.PurchaseOrderLineId)))
        {
            throw new InvalidOperationException("One or more PO lines are invalid for this transaction.");
        }

        var existing = await _db.InspectionLines.Where(l => l.InwardTransactionId == id).ToListAsync();
        _db.InspectionLines.RemoveRange(existing);

        foreach (var line in request.Lines)
        {
            _db.InspectionLines.Add(new InspectionLine
            {
                InwardTransactionId = id,
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                ReceivedQty = line.ReceivedQty,
                Condition = line.Condition,
                Notes = line.Notes
            });
        }

        transaction.Status = InwardStatus.Inspecting;
        await _db.SaveChangesAsync();

        var exceptionLines = request.Lines.Count(l => l.Condition != MaterialCondition.Ok);
        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Inspection recorded for vehicle '{transaction.Vehicle?.Number}' - {request.Lines.Count} line(s){(exceptionLines > 0 ? $", {exceptionLines} with exceptions" : "")}.",
            supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    public async Task<InwardJobDto> CompleteAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != InwardStatus.Inspecting)
        {
            throw new InvalidOperationException("Job must be in inspection before it can be completed.");
        }

        if (transaction.Photos.Count == 0)
        {
            throw new InvalidOperationException("At least one photo is required before completing.");
        }

        if (transaction.InspectionLines.Count == 0)
        {
            throw new InvalidOperationException("Inspection must be recorded before completing.");
        }

        var hasExceptions = transaction.InspectionLines.Any(l => l.Condition != MaterialCondition.Ok);
        var grnNumber = $"GRN-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

        transaction.DockOutTime = DateTime.UtcNow;
        transaction.Status = InwardStatus.Completed;

        _db.GoodsReceiptNotes.Add(new GoodsReceiptNote
        {
            InwardTransactionId = id,
            GrnNumber = grnNumber,
            GeneratedAt = DateTime.UtcNow,
            HasExceptions = hasExceptions
        });

        await _db.SaveChangesAsync();
        await _vehicleLogisticsSync.MarkCompletedAsync(transaction.Vehicle?.Number, transaction.WarehouseId);

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Inward completed for vehicle '{transaction.Vehicle?.Number}' - {grnNumber}{(hasExceptions ? " (exceptions flagged)" : "")}.",
            supervisorUserId);

        if (hasExceptions)
        {
            // A real, visible Office to-do (surfaced on the Follow-ups page) - the log line
            // stays as an ops-side trace. Email/SMS to the supplier remains future work.
            var exceptionSummary = string.Join("; ", transaction.InspectionLines
                .Where(l => l.Condition != MaterialCondition.Ok)
                .GroupBy(l => l.Condition)
                .Select(g => $"{g.Key}: {g.Sum(l => l.ReceivedQty):0.##}"));
            _db.FollowUpTasks.Add(new FollowUpTask
            {
                Type = FollowUpType.InwardException,
                EntityName = "InwardTransaction",
                EntityId = id,
                WarehouseId = transaction.WarehouseId,
                Title = $"{grnNumber} flagged exceptions - supplier follow-up",
                Details = $"Vehicle {transaction.Vehicle?.Number}, PO {transaction.PurchaseOrder?.PONumber}. Exception quantities - {exceptionSummary}.",
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("FollowUpsChanged");

            _logger.LogWarning("Inward transaction {Id} completed with exceptions requiring supplier follow-up.", id);
        }

        return await BroadcastAndReturn(id);
    }

    private async Task<InwardTransaction> GetOwnedTransactionAsync(int id, string supervisorUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.AssignedSupervisorUserId != supervisorUserId)
        {
            throw new UnauthorizedAccessException("This job is not assigned to you.");
        }

        return transaction;
    }

    private async Task<InwardTransaction> GetOwnedByGateSecurityAsync(int id, string securityUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.GateInBySecurityUserId != securityUserId)
        {
            throw new UnauthorizedAccessException("This job was not checked in by you.");
        }

        if (transaction.Status != InwardStatus.GateIn)
        {
            throw new InvalidOperationException("Gate documents/photos can only be added while the vehicle is still at the gate.");
        }

        return transaction;
    }

    private async Task<InwardJobDto> BroadcastAndReturn(int id)
    {
        var dto = await GetByIdAsync(id) ?? throw new InvalidOperationException("Transaction disappeared unexpectedly.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("JobUpdated", dto);
        return dto;
    }

    private static InwardJobDto MapToDto(InwardTransaction t) => new(
        t.Id,
        t.Vehicle!.Number,
        t.InwardTxnNumber,
        t.PurchaseOrder!.PONumber,
        t.PurchaseOrder.SupplierName,
        t.Status.ToString(),
        t.GateInTime,
        t.DriverName,
        t.DriverMobile,
        t.TransporterName,
        t.GateName,
        t.GpsLatitude,
        t.GpsLongitude,
        t.IsNewVehicle,
        t.HasDeliveryDateMismatch,
        t.AssignedSupervisorUserId,
        t.AssignedTime,
        t.BayName,
        t.DockInTime,
        t.UnloadingStartTime,
        t.DockOutTime,
        t.PurchaseOrder.Lines.Select(l => new PoLineDto(l.Id, l.ProductName, l.ExpectedQty, l.PickListQty, l.LoadedQty, l.UnitOfMeasure)).ToList(),
        t.Photos.Select(p => new PhotoDto(p.Id, p.Type.ToString(), p.FilePath, p.CapturedAt)).ToList(),
        t.Documents.Select(d => new DocumentDto(d.Id, d.Type.ToString(), d.FilePath, d.UploadedAt)).ToList(),
        t.InspectionLines.Select(l => new InspectionLineDto(
            l.Id, l.PurchaseOrderLineId, l.PurchaseOrderLine!.ProductName, l.PurchaseOrderLine.ExpectedQty,
            l.ReceivedQty, l.Condition.ToString(), l.Notes)).ToList(),
        t.Grn is null ? null : new GrnDto(t.Grn.GrnNumber, t.Grn.GeneratedAt, t.Grn.HasExceptions),
        t.Remarks,
        t.GateOutTime,
        t.GatePassToken);
}

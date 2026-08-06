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
            .Include(t => t.UnplannedLines).ThenInclude(l => l.Product)
            .Include(t => t.Grn);

    public async Task<InwardJobDto> CheckInAsync(GateCheckInRequest request, string securityUserId)
    {
        // Attributes the transaction to a warehouse purely from the checking-in Security user's own
        // WarehouseId - no mobile app change needed, the gate-in form never asks for a warehouse.
        var warehouseId = await _db.Users
            .Where(u => u.Id == securityUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        var alreadyUsed = await _db.InwardTransactions
            .AnyAsync(t => t.InwardTxnNumber == request.InwardTxnNumber);
        if (alreadyUsed)
        {
            throw new InvalidOperationException($"Inward transaction number '{request.InwardTxnNumber}' has already been used.");
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == request.VehicleNumber);
        var isNewVehicle = vehicle is null;
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = request.VehicleNumber };
            _db.Vehicles.Add(vehicle);
        }

        // A plate auto-created (or still missing capacity from an earlier gate-in) gets Vehicle's
        // own generic defaults for whichever fields are blank - mirrors
        // OutwardService.BackfillVehicleCapacityAsync's identical backfill on the Outward side, so
        // it never sits blocked at "Missing" capacity in the Vehicle Registry by accident.
        vehicle.MaxWeightKg ??= Vehicle.DefaultMaxWeightKg;
        vehicle.LengthCm ??= Vehicle.DefaultLengthCm;
        vehicle.WidthCm ??= Vehicle.DefaultWidthCm;
        vehicle.HeightCm ??= Vehicle.DefaultHeightCm;

        // Security's job at the gate is to log the physical arrival - Vehicle Number, driver,
        // photos, documents - not to match it against a Dispatch Plan PO. The PO Number Security
        // types is kept purely as a hint (SecurityEnteredPoNumber, not validated); the job is
        // created with no PurchaseOrder link at all. Office links it to a real Dispatch Plan entry
        // afterward from the Expected tab (see LinkVehicleAsync below) - only then does it have PO
        // lines a Supervisor can actually inspect against.
        var transaction = new InwardTransaction
        {
            Vehicle = vehicle,
            WarehouseId = warehouseId,
            InwardTxnNumber = request.InwardTxnNumber,
            PurchaseOrderId = null,
            SecurityEnteredPoNumber = NullIfEmpty(request.PONumber),
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
            Remarks = request.Remarks
        };

        _db.InwardTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", transaction.Id, AuditAction.Created,
            $"Vehicle '{request.VehicleNumber}' gate-checked-in" +
            (string.IsNullOrWhiteSpace(request.PONumber) ? "" : $" (PO noted: '{request.PONumber}')") +
            (string.IsNullOrWhiteSpace(request.GateName) ? "" : $" at {request.GateName}") +
            " - not yet linked to a Dispatch Plan entry.",
            securityUserId);

        var dto = await GetByIdAsync(transaction.Id) ?? throw new InvalidOperationException("Failed to load created transaction.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("JobAvailable", dto);
        return dto;
    }

    // Office's "Link Vehicle" action: attaches an already-gated-in (Security-created) Inward Job to
    // a real Dispatch Plan entry, picked from the Expected tab. This is the only place a
    // PurchaseOrder ever gets attached to a job created by CheckInAsync above - the job keeps its
    // existing driver/photos/documents/etc. (it's the SAME transaction, just no longer missing a PO).
    public async Task<InwardJobDto> LinkVehicleAsync(LinkVehicleRequest request, string officeUserId)
    {
        var warehouseId = await _db.Users
            .Where(u => u.Id == officeUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();
        if (warehouseId is null)
        {
            throw new InvalidOperationException("Your account is not assigned to a warehouse.");
        }

        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == request.InwardTransactionId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.WarehouseId != warehouseId)
        {
            throw new UnauthorizedAccessException("This job is not in your warehouse.");
        }

        if (transaction.PurchaseOrderId is not null)
        {
            throw new InvalidOperationException("This vehicle is already linked to a Dispatch Plan entry.");
        }

        var poNumber = request.PoNumber.Trim();
        var vehicleNumber = transaction.Vehicle!.Number;

        var claim = await TryClaimDispatchPlanForLinkingAsync(
            poNumber, warehouseId.Value, vehicleNumber,
            transaction.TransporterName, transaction.DriverName, transaction.DriverMobile, null);
        if (claim is null)
        {
            throw new InvalidOperationException($"No untagged Dispatch Plan rows found for PO '{poNumber}' addressed to your warehouse.");
        }
        var (po, claimedDispatchPlanRows) = claim.Value;

        transaction.PurchaseOrderId = po.Id;
        transaction.HasDeliveryDateMismatch = po.ExpectedDeliveryDate.HasValue
            && Math.Abs((DateTime.UtcNow.Date - po.ExpectedDeliveryDate.Value.Date).TotalDays) > DeliveryDateToleranceDays;
        await _db.SaveChangesAsync();

        foreach (var row in claimedDispatchPlanRows)
        {
            row.ConsumedByInwardTransactionId = transaction.Id;
        }
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", transaction.Id, AuditAction.Updated,
            $"Vehicle '{vehicleNumber}' linked by Office to PO '{poNumber}'.", officeUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Claims untagged Dispatch Plan rows for a PO Number + warehouse, setting VehicleNumber/
    // TransporterName/DriverName/DriverPhone/VehicleType on them as part of the same atomic claim -
    // used by LinkVehicleAsync above to attach an already-arrived vehicle's known details onto the
    // Dispatch Plan rows it's being linked to. Only matches rows that haven't already been linked
    // (VehicleNumber == null) - rows a Logistics Manager already gave a vehicle number to directly
    // in the Excel (still possible, just no longer required) aren't matched here since there's no
    // Security-side flow left that looks them up by vehicle number anymore.
    private async Task<(PurchaseOrder Po, List<VehicleLogisticsRecord> ClaimedRows)?> TryClaimDispatchPlanForLinkingAsync(
        string poNumber, int warehouseId, string vehicleNumber,
        string? transporterName, string? driverName, string? driverPhone, string? vehicleType)
    {
        var matched = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse)
            .Where(r => r.VehicleNumber == null && r.PoNumber == poNumber
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

        ValidateBoxQuantities(matched);
        var matchedIds = matched.Select(r => r.Id).ToList();

        var claimedCount = await _db.VehicleLogisticsRecords
            .Where(r => matchedIds.Contains(r.Id) && r.InwardClaimStartedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.InwardClaimStartedAtUtc, DateTime.UtcNow)
                .SetProperty(r => r.Status, VehicleLogisticsStatus.InProgress)
                .SetProperty(r => r.VehicleNumber, vehicleNumber)
                .SetProperty(r => r.TransporterName, transporterName)
                .SetProperty(r => r.DriverName, driverName)
                .SetProperty(r => r.DriverPhone, driverPhone)
                .SetProperty(r => r.VehicleType, vehicleType));

        if (claimedCount != matchedIds.Count)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan rows for PO '{poNumber}' are already being tagged by another request.");
        }

        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        // ExecuteUpdateAsync bypasses change tracking, so the in-memory rows still show their old
        // (null) VehicleNumber - patch locally so PO synthesis below reflects the tag.
        foreach (var row in matched)
        {
            row.VehicleNumber = vehicleNumber;
        }

        var po = await SynthesizePurchaseOrderFromDispatchRowsAsync(matched, poNumber, vehicleNumber);
        return (po, matched);
    }

    private static void ValidateBoxQuantities(List<VehicleLogisticsRecord> matched)
    {
        var zeroQtyRows = matched.Where(r => r.BoxQuantity <= 0).Select(r => r.Sku).ToList();
        if (zeroQtyRows.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan row(s) for SKU(s) {string.Join(", ", zeroQtyRows)} have no box quantity set - fix them before checking in.");
        }
    }

    // Suffixed with the vehicle number - PONumber has a unique index, and the same real-world PO
    // number can legitimately appear across multiple vehicles (split shipments), so the raw
    // PONumber can't be reused as the synthesized PO's own number without risking a collision.
    private async Task<PurchaseOrder> SynthesizePurchaseOrderFromDispatchRowsAsync(
        List<VehicleLogisticsRecord> matched, string poNumber, string vehicleNumber)
    {
        var (dispatchQtyByRowId, extraLines) = await ResolveDispatchQuantitiesAsync(matched);
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
                    UnitOfMeasure = "PCS",
                    SourceDispatchOrderLineId = qty?.SourceDispatchOrderLineId
                };
            }).Concat(extraLines.Select(l => new PurchaseOrderLine
            {
                ProductName = l.ProductName,
                // No Dispatch Plan row exists for this SKU at all - the source warehouse's
                // supervisor added it during loading (3D Load Plan Workspace "Add SKU"), beyond
                // what Logistics/Office originally planned - so there's no planned quantity to
                // fall back to, only what was actually loaded (or the pick-list qty if loading
                // hasn't happened yet).
                ExpectedQty = l.LoadedQty ?? l.OrderedQty,
                PickListQty = l.OrderedQty,
                LoadedQty = l.LoadedQty,
                UnitOfMeasure = "PCS",
                IsExtra = true,
                SourceDispatchOrderLineId = l.DispatchOrderLineId
            })).ToList()
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        return po;
    }

    // Self-healing backfill for a job whose PurchaseOrder was already synthesized (Office already
    // linked the vehicle) before the source supervisor added a SKU that has no Dispatch Plan row -
    // SynthesizePurchaseOrderFromDispatchRowsAsync only ever runs once, at link time, so without
    // this an extra SKU added afterward (or one added before this fix existed) would never surface
    // here. Called on every single-job fetch (GetByIdAsync/GetByIdForOfficeAsync) rather than only
    // at link time, so it also catches SKUs added after the fact without needing a re-link.
    private async Task SyncExtraLinesAsync(InwardTransaction transaction)
    {
        if (transaction.PurchaseOrder is null)
        {
            return;
        }

        var outwardTransactionId = await _db.VehicleLogisticsRecords
            .Where(r => r.ConsumedByInwardTransactionId == transaction.Id && r.ConsumedByOutwardTransactionId != null)
            .Select(r => r.ConsumedByOutwardTransactionId!.Value)
            .Distinct()
            .FirstOrDefaultAsync();

        if (outwardTransactionId == 0)
        {
            return;
        }

        // Matched by DispatchOrderLine.Id, not ProductName - two "extra" lines (e.g. the supervisor
        // clicks "Add SKU" twice for the same product) can share a name, and a name-based diff
        // would mistake the second for already-mirrored the moment the first gets synced.
        var mirroredDispatchOrderLineIds = transaction.PurchaseOrder.Lines
            .Where(l => l.SourceDispatchOrderLineId.HasValue)
            .Select(l => l.SourceDispatchOrderLineId!.Value)
            .ToHashSet();

        var dispatchLines = await _db.OutwardTransactions
            .Where(t => t.Id == outwardTransactionId)
            .SelectMany(t => t.DispatchOrder!.Lines)
            .ToListAsync();

        var missingLines = dispatchLines.Where(l => l.IsExtra && !mirroredDispatchOrderLineIds.Contains(l.Id)).ToList();
        if (missingLines.Count == 0)
        {
            return;
        }

        var loadLines = await _db.OutwardLoadLines
            .Where(l => l.OutwardTransactionId == outwardTransactionId)
            .ToListAsync();

        foreach (var line in missingLines)
        {
            var loadedQty = loadLines.FirstOrDefault(l => l.DispatchOrderLineId == line.Id)?.LoadedQty;
            transaction.PurchaseOrder.Lines.Add(new PurchaseOrderLine
            {
                ProductName = line.ProductName,
                ExpectedQty = loadedQty ?? line.OrderedQty,
                PickListQty = line.OrderedQty,
                LoadedQty = loadedQty,
                UnitOfMeasure = "PCS",
                IsExtra = true,
                SourceDispatchOrderLineId = line.Id
            });
        }

        await _db.SaveChangesAsync();
    }

    private sealed record DispatchQuantities(decimal? PickListQty, decimal? LoadedQty, int? SourceDispatchOrderLineId);

    // A DispatchOrderLine the source warehouse's supervisor added during loading (OutwardService.
    // AddDispatchOrderLineAsync, IsExtra == true) - see ResolveDispatchQuantitiesAsync.
    private sealed record ExtraDispatchLine(int DispatchOrderLineId, string ProductName, decimal OrderedQty, decimal? LoadedQty);

    // "Expected material" at Inward shows three distinct numbers side by side, each reflecting a
    // later stage of the same shipment's journey at the source warehouse's Outward job (if one
    // already happened - the normal real-world sequence: Outward claims and dispatches first,
    // Inward receives later): the originally planned Dispatch Plan box quantity (ExpectedQty,
    // set directly from the row above), the quantity Office actually generated the pick list
    // with (PickListQty - only exists once "Generate Pick List" has run), and what the
    // supervisor actually loaded onto the truck (LoadedQty - only exists once load lines are
    // submitted). Both are null for a row that was never claimed by an Outward job, or hasn't
    // reached that stage yet - the UI shows a dash rather than fabricating a value.
    //
    // Also returns every DispatchOrderLine on those same Outward transactions with IsExtra == true -
    // SKUs the supervisor added during loading that were never part of the Dispatch Plan, so
    // they'd otherwise never reach the destination's Expected material list at all (see
    // SynthesizePurchaseOrderFromDispatchRowsAsync). Matching is by DispatchOrderLine.Id/IsExtra,
    // never by ProductName - two lines (one planned, one supervisor-added, or two supervisor-added)
    // can legitimately share the same product name, and a name-based diff would silently drop one.
    private async Task<(Dictionary<int, DispatchQuantities> ByRowId, List<ExtraDispatchLine> ExtraLines)> ResolveDispatchQuantitiesAsync(List<VehicleLogisticsRecord> rows)
    {
        var outwardTransactionIds = rows
            .Where(r => r.ConsumedByOutwardTransactionId.HasValue)
            .Select(r => r.ConsumedByOutwardTransactionId!.Value)
            .Distinct()
            .ToList();

        if (outwardTransactionIds.Count == 0)
        {
            return (new Dictionary<int, DispatchQuantities>(), new List<ExtraDispatchLine>());
        }

        var pickListLines = await _db.OutwardTransactions
            .Where(t => outwardTransactionIds.Contains(t.Id))
            .SelectMany(t => t.DispatchOrder!.Lines, (t, line) => new
            {
                OutwardTransactionId = t.Id,
                DispatchOrderLineId = line.Id,
                line.ProductName,
                line.OrderedQty,
                line.IsExtra
            })
            .ToListAsync();

        var loadLines = await _db.OutwardLoadLines
            .Where(l => outwardTransactionIds.Contains(l.OutwardTransactionId))
            .ToListAsync();

        var result = new Dictionary<int, DispatchQuantities>();
        foreach (var row in rows)
        {
            if (row.ConsumedByOutwardTransactionId is not { } outwardTransactionId)
            {
                continue;
            }

            var matchedLine = pickListLines
                .FirstOrDefault(l => l.OutwardTransactionId == outwardTransactionId && !l.IsExtra && l.ProductName == row.Sku);
            var loadedQty = matchedLine is null
                ? null
                : loadLines.FirstOrDefault(ll => ll.OutwardTransactionId == outwardTransactionId && ll.DispatchOrderLineId == matchedLine.DispatchOrderLineId)?.LoadedQty;

            result[row.Id] = new DispatchQuantities(matchedLine?.OrderedQty, loadedQty, matchedLine?.DispatchOrderLineId);
        }

        var extraLines = pickListLines
            .Where(l => l.IsExtra)
            .Select(l => new ExtraDispatchLine(
                l.DispatchOrderLineId, l.ProductName, l.OrderedQty,
                loadLines.FirstOrDefault(ll => ll.OutwardTransactionId == l.OutwardTransactionId && ll.DispatchOrderLineId == l.DispatchOrderLineId)?.LoadedQty))
            .ToList();

        return (result, extraLines);
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
            query = query.Where(t => t.PurchaseOrder != null && t.PurchaseOrder.PONumber.Contains(poNumber));
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
            query = query.Where(t => t.PurchaseOrder != null && t.PurchaseOrder.PONumber.Contains(poNumber));
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
        if (transaction is null)
        {
            return null;
        }
        await SyncExtraLinesAsync(transaction);
        return MapToDto(transaction);
    }

    public async Task<InwardJobDto?> GetByIdAsync(int id)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return null;
        }
        await SyncExtraLinesAsync(transaction);
        return MapToDto(transaction);
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

        if (transaction.PurchaseOrderId is null)
        {
            throw new InvalidOperationException("This vehicle isn't linked to a Dispatch Plan entry yet - Office needs to link it from the Expected tab before a supervisor can be assigned.");
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

    // Backs the Supervisor-facing SKU search on the Mismatch SKU Details section - api/admin/products
    // is SuperAdmin-only, so this exposes a read-only subset (org-scoped automatically via the
    // ITenantScoped query filter on Product) to the Supervisor role instead.
    public async Task<List<SkuMasterItemDto>> SearchSkuMasterAsync(string? search)
    {
        var query = _db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || p.SkuCode.Contains(search));
        }

        return await query
            .OrderBy(p => p.Name)
            .Take(50)
            .Select(p => new SkuMasterItemDto(p.Id, p.Name, p.SkuCode))
            .ToListAsync();
    }

    private const int MaxPhotosPerSkuLine = 2;

    public async Task<InwardJobDto> AddPhotoAsync(int id, string supervisorUserId, PhotoType type, string fileName, Stream content, int? purchaseOrderLineId = null)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (InwardStatus.Docked or InwardStatus.Inspecting))
        {
            throw new InvalidOperationException("Photos can only be added after dock-in and before completion.");
        }

        if (purchaseOrderLineId is int lineId)
        {
            if (transaction.PurchaseOrder!.Lines.All(l => l.Id != lineId))
            {
                throw new InvalidOperationException("This PO line is invalid for this transaction.");
            }

            var existingForLine = transaction.Photos.Count(p => p.Type == PhotoType.SkuCondition && p.PurchaseOrderLineId == lineId);
            if (existingForLine >= MaxPhotosPerSkuLine)
            {
                throw new InvalidOperationException($"Only {MaxPhotosPerSkuLine} photos are allowed per SKU.");
            }
        }

        var filePath = await _photoStorage.SaveAsync($"inward-{id}", fileName, content);

        _db.PhotoEvidences.Add(new PhotoEvidence
        {
            InwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow,
            PurchaseOrderLineId = purchaseOrderLineId
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

        await ReplaceInspectionDataAsync(transaction, request);
        transaction.Status = InwardStatus.Inspecting;
        await _db.SaveChangesAsync();

        var exceptionLines = request.Lines.Count(l => l.Condition != MaterialCondition.Ok);
        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Inspection recorded for vehicle '{transaction.Vehicle?.Number}' - {request.Lines.Count} line(s){(exceptionLines > 0 ? $", {exceptionLines} with exceptions" : "")}.",
            supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    // Lets Office correct the Supervisor's recorded quantities/Mismatch SKUs before generating the
    // GRN - same request shape and validation as SubmitInspectionAsync above, just usable while
    // PendingOfficeVerification instead of Docked/Inspecting, and without the assigned-supervisor
    // ownership check (Office reviewing a job isn't the assigned Supervisor).
    public async Task<InwardJobDto> UpdateInspectionAsync(int id, string officeUserId, SubmitInspectionRequest request)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.Status != InwardStatus.PendingOfficeVerification)
        {
            throw new InvalidOperationException("Inspection details can only be corrected while pending Office verification.");
        }

        await ReplaceInspectionDataAsync(transaction, request);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Inspection details corrected by Office for vehicle '{transaction.Vehicle?.Number}' prior to GRN generation.",
            officeUserId);

        return await BroadcastAndReturn(id);
    }

    // Shared by Supervisor's original submission and Office's pre-GRN correction - validates PO
    // line ids, Mismatch SKU Details SKUs, and the Mismatch-vs-Mismatch-SKU-Details total match,
    // then full-replaces both InspectionLines and UnplannedReceiptLines for the transaction. Does
    // not save or touch Status - callers own both.
    private async Task ReplaceInspectionDataAsync(InwardTransaction transaction, SubmitInspectionRequest request)
    {
        var id = transaction.Id;

        var validLineIds = transaction.PurchaseOrder!.Lines.Select(l => l.Id).ToHashSet();
        if (request.Lines.Count == 0 || request.Lines.Any(l => !validLineIds.Contains(l.PurchaseOrderLineId)))
        {
            throw new InvalidOperationException("One or more PO lines are invalid for this transaction.");
        }

        var unplannedLines = request.UnplannedLines ?? new List<UnplannedReceiptLineRequest>();
        var productNames = unplannedLines.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Products
                .Where(p => unplannedLines.Select(l => l.ProductId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Name);
        if (unplannedLines.Any(l => !productNames.ContainsKey(l.ProductId)))
        {
            throw new InvalidOperationException("One or more Mismatch SKU Details SKUs were not found in the SKU Master.");
        }

        // Mismatch SKU Details doesn't have to name the same SKU as the PO line it's
        // corroborating (any SKU from the Master may be picked) - only the totals need to agree:
        // everything entered as Mismatch anywhere in the inspection breakdown must be accounted
        // for, in aggregate, by the Mismatch SKU Details rows.
        var totalMismatchQty = request.Lines
            .Where(l => l.Condition == MaterialCondition.Mismatch)
            .Sum(l => l.ReceivedQty);
        var totalDetailQty = unplannedLines.Sum(l => l.Quantity);

        if (totalDetailQty != totalMismatchQty)
        {
            throw new InvalidOperationException(
                $"Mismatch SKU Details totals {totalDetailQty:0.##}, but {totalMismatchQty:0.##} was entered as Mismatch in the inspection breakdown - they must match.");
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

        var existingUnplanned = await _db.UnplannedReceiptLines.Where(l => l.InwardTransactionId == id).ToListAsync();
        _db.UnplannedReceiptLines.RemoveRange(existingUnplanned);

        foreach (var line in unplannedLines)
        {
            _db.UnplannedReceiptLines.Add(new UnplannedReceiptLine
            {
                InwardTransactionId = id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                Notes = line.Notes
            });
        }
    }

    // Supervisor's "Complete Unloading" action - records that unloading/inspection is done, but no
    // longer generates the GRN itself (moved to Office's VerifyAndGenerateGrnAsync below). The job
    // sits at PendingOfficeVerification until Office reviews and confirms it.
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

        transaction.DockOutTime = DateTime.UtcNow;
        transaction.Status = InwardStatus.PendingOfficeVerification;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
            $"Unloading completed for vehicle '{transaction.Vehicle?.Number}' - pending Office verification.",
            supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    // Office's confirmation step: reviews the inspection + Mismatch SKU Details Supervisor
    // recorded, then generates the GRN - this is the only place a GoodsReceiptNote gets created now
    // (moved out of Supervisor's CompleteAsync above, per the requirement that unloading completion
    // alone must not produce a GRN).
    public async Task<InwardJobDto> VerifyAndGenerateGrnAsync(int id, string officeUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.Status != InwardStatus.PendingOfficeVerification)
        {
            throw new InvalidOperationException("Job must be pending Office verification before a GRN can be generated.");
        }

        var hasExceptions = transaction.InspectionLines.Any(l => l.Condition != MaterialCondition.Ok);
        var grnNumber = $"GRN-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

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
            $"Inward verified and GRN generated by Office for vehicle '{transaction.Vehicle?.Number}' - {grnNumber}{(hasExceptions ? " (exceptions flagged)" : "")}.",
            officeUserId);

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
                OrganizationId = transaction.OrganizationId,
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
        t.PurchaseOrder?.PONumber,
        t.SecurityEnteredPoNumber,
        t.PurchaseOrder?.SupplierName,
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
        t.PurchaseOrder?.Lines.Select(l => new PoLineDto(l.Id, l.ProductName, l.ExpectedQty, l.PickListQty, l.LoadedQty, l.UnitOfMeasure, l.IsExtra)).ToList() ?? new List<PoLineDto>(),
        t.Photos.Select(p => new PhotoDto(p.Id, p.Type.ToString(), p.FilePath, p.CapturedAt, p.PurchaseOrderLineId)).ToList(),
        t.Documents.Select(d => new DocumentDto(d.Id, d.Type.ToString(), d.FilePath, d.UploadedAt)).ToList(),
        t.InspectionLines.Select(l => new InspectionLineDto(
            l.Id, l.PurchaseOrderLineId, l.PurchaseOrderLine!.ProductName, l.PurchaseOrderLine.ExpectedQty,
            l.ReceivedQty, l.Condition.ToString(), l.Notes)).ToList(),
        t.UnplannedLines.Select(l => new UnplannedReceiptLineDto(l.Id, l.ProductId, l.Product!.Name, l.Product.SkuCode, l.Quantity, l.Notes)).ToList(),
        t.Grn is null ? null : new GrnDto(t.Grn.GrnNumber, t.Grn.GeneratedAt, t.Grn.HasExceptions),
        t.Remarks,
        t.GateOutTime,
        t.GatePassToken);
}

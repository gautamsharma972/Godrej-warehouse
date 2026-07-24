using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;
using WarehouseGate.Infrastructure.Storage;
using WarehouseGate.LoadPlanning;
using WarehouseGate.LoadPlanning.Models;

namespace WarehouseGate.Api.Services;

public class OutwardService
{
    private readonly WarehouseGateDbContext _db;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IHubContext<InwardHub> _hub;
    private readonly ILogger<OutwardService> _logger;
    private readonly LoadPlanningEngine _loadPlanningEngine;
    private readonly OutwardLoadPlanService _loadPlanService;
    private readonly VehicleLogisticsSyncService _vehicleLogisticsSync;
    private readonly AuditService _audit;

    public OutwardService(
        WarehouseGateDbContext db,
        IPhotoStorageService photoStorage,
        IHubContext<InwardHub> hub,
        ILogger<OutwardService> logger,
        LoadPlanningEngine loadPlanningEngine,
        OutwardLoadPlanService loadPlanService,
        VehicleLogisticsSyncService vehicleLogisticsSync,
        AuditService audit)
    {
        _db = db;
        _photoStorage = photoStorage;
        _hub = hub;
        _logger = logger;
        _loadPlanningEngine = loadPlanningEngine;
        _loadPlanService = loadPlanService;
        _vehicleLogisticsSync = vehicleLogisticsSync;
        _audit = audit;
    }

    private IQueryable<OutwardTransaction> Query() =>
        _db.OutwardTransactions
            .Include(t => t.DispatchOrder!).ThenInclude(d => d.Lines).ThenInclude(l => l.Product)
            .Include(t => t.Vehicle)
            .Include(t => t.Photos)
            .Include(t => t.LoadLines).ThenInclude(l => l.DispatchOrderLine)
            .Include(t => t.DispatchNote)
            .AsSplitQuery();

    // LoadPlanTotalSteps/LoadPlanResolvedSteps only need 2 counts per transaction, not the full
    // OutwardLoadPlanGroup rows (position/dims/orientation etc. for every placed carton group) -
    // eager-loading those via Include used to multiply badly on list/history endpoints (every
    // completed job that ever had a load plan pulled its whole group graph into memory on every
    // fetch). Aggregating in SQL instead keeps this endpoint's cost tied to the number of groups
    // summed server-side, not the number of columns materialized into .NET objects.
    private async Task<Dictionary<int, (int Total, int Resolved)>> GetLoadPlanProgressAsync(IReadOnlyCollection<int> transactionIds)
    {
        if (transactionIds.Count == 0)
        {
            return new Dictionary<int, (int, int)>();
        }

        var rows = await _db.OutwardLoadPlanGroups
            .Where(g => g.OutwardLoadPlanOption!.IsSelected && transactionIds.Contains(g.OutwardLoadPlanOption.OutwardTransactionId))
            .GroupBy(g => g.OutwardLoadPlanOption!.OutwardTransactionId)
            .Select(grp => new
            {
                TransactionId = grp.Key,
                Total = grp.Count(),
                Resolved = grp.Count(g => g.ConfirmationStatus == LoadGroupConfirmationStatus.Loaded
                    || g.ConfirmationStatus == LoadGroupConfirmationStatus.Mismatch
                    || g.ConfirmationStatus == LoadGroupConfirmationStatus.ShortLoad
                    || g.ConfirmationStatus == LoadGroupConfirmationStatus.Skipped)
            })
            .ToListAsync();

        return rows.ToDictionary(r => r.TransactionId, r => (r.Total, r.Resolved));
    }

    public async Task<OutwardJobDto> GeneratePickListAsync(GeneratePickListRequest request, string officeUserId)
    {
        var dispatchOrder = await _db.DispatchOrders.Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == request.DispatchOrderId)
            ?? throw new InvalidOperationException($"Dispatch order {request.DispatchOrderId} was not found.");

        var alreadyActive = await _db.OutwardTransactions
            .AnyAsync(t => t.DispatchOrderId == request.DispatchOrderId && t.Status != OutwardStatus.Completed);
        if (alreadyActive)
        {
            throw new InvalidOperationException("A pick list for this dispatch order is already in progress.");
        }

        var transaction = new OutwardTransaction
        {
            DispatchOrderId = dispatchOrder.Id,
            Status = OutwardStatus.PickListGenerated,
            CreatedTime = DateTime.UtcNow,
            CreatedByOfficeUserId = officeUserId
        };

        _db.OutwardTransactions.Add(transaction);
        await _db.SaveChangesAsync();

        transaction.OutwardTxnNumber = $"OUT-{DateTime.UtcNow:yyyyMMdd}-{transaction.Id:D4}";
        await _db.SaveChangesAsync();

        var dto = await GetByIdAsync(transaction.Id) ?? throw new InvalidOperationException("Failed to load created transaction.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("OutwardJobAvailable", dto);
        return dto;
    }

    // Bridges the Logistics Manager's Dispatch Plan upload into the real Outward workflow: finds
    // the matching VehicleLogisticsRecord group (this vehicle, From = the caller's own warehouse,
    // still InTransit), synthesizes a DispatchOrder + lines from it (Product-matched by SkuCode -
    // required, since the 3D load planner hard-requires a linked Product for placement dimensions),
    // then hands off to GeneratePickListAsync unchanged.
    public async Task<OutwardJobDto> GeneratePickListFromDispatchPlanAsync(string vehicleNumber, int officeWarehouseId, string officeUserId)
    {
        var matched = await _db.VehicleLogisticsRecords
            .Include(r => r.ToWarehouse)
            .Where(r => r.VehicleNumber == vehicleNumber && r.FromWarehouseId == officeWarehouseId
                && r.Status == VehicleLogisticsStatus.InTransit)
            .ToListAsync();

        if (matched.Count == 0)
        {
            throw new InvalidOperationException($"No pending Dispatch Plan rows found for vehicle '{vehicleNumber}'.");
        }

        // Office's own PickListQuantity override (if set) wins over the Logistics Manager's
        // planned BoxQuantity - see the field's doc comment on the domain model.
        var zeroQtyRows = matched.Where(r => (r.PickListQuantity ?? r.BoxQuantity) <= 0).Select(r => r.Sku).ToList();
        if (zeroQtyRows.Count > 0)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan row(s) for SKU(s) {string.Join(", ", zeroQtyRows)} have no box quantity set - fix them before generating a pick list.");
        }

        var productByRowId = await ResolveProductsForDispatchPlanRowsAsync(matched);

        var matchedIds = matched.Select(r => r.Id).ToList();

        // Atomic claim - see InwardService.TryClaimDispatchPlanForInwardAsync for the identical
        // reasoning (short, immediately-committed transaction so this method's own realtime
        // broadcast, fired later inside GeneratePickListAsync, isn't delayed behind a longer-lived
        // ambient transaction).
        var claimedCount = await _db.VehicleLogisticsRecords
            .Where(r => matchedIds.Contains(r.Id) && r.Status == VehicleLogisticsStatus.InTransit)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, VehicleLogisticsStatus.InProgress));

        if (claimedCount != matchedIds.Count)
        {
            throw new InvalidOperationException(
                $"Dispatch Plan rows for vehicle '{vehicleNumber}' are already being processed by another request.");
        }

        // Lets the Logistics Manager's own Dispatch Plan list and Office's pending panels drop this
        // group live the moment it's claimed, instead of waiting for their next unrelated refresh.
        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        var toWarehouseName = matched[0].ToWarehouse!.Name;
        var dispatchOrder = new DispatchOrder
        {
            DispatchOrderNumber = $"DP-{DateTime.UtcNow:yyyyMMdd}-{vehicleNumber}-{DateTime.UtcNow.Ticks % 10000}",
            CustomerName = toWarehouseName,
            RequestedDate = matched.Any(r => r.EtaDateTime.HasValue)
                ? matched.Where(r => r.EtaDateTime.HasValue).Min(r => r.EtaDateTime!.Value)
                : DateTime.UtcNow,
            Lines = matched.Select(r => new DispatchOrderLine
            {
                ProductName = r.Sku,
                OrderedQty = r.PickListQuantity ?? r.BoxQuantity,
                UnitOfMeasure = "PCS",
                ProductId = productByRowId[r.Id].Id,
                DeliveryLocation = toWarehouseName
            }).ToList()
        };
        _db.DispatchOrders.Add(dispatchOrder);
        await _db.SaveChangesAsync();

        var dto = await GeneratePickListAsync(new GeneratePickListRequest(dispatchOrder.Id), officeUserId);

        // GeneratePickListAsync never sets WarehouseId (only real gate check-in does, later) - but
        // Office needs to assign a supervisor to a job it just generated, before any truck has
        // physically arrived, and both the Office job list and AssignSupervisorAsync are scoped on
        // WarehouseId. Deliberate, documented fix: Office's own warehouse genuinely IS this
        // transaction's origin warehouse in the Dispatch-Plan-driven flow, unlike the old model
        // where the origin wasn't knowable until gate-in.
        var createdTransaction = await _db.OutwardTransactions.FirstAsync(t => t.Id == dto.Id);
        createdTransaction.WarehouseId = officeWarehouseId;

        // Same reasoning as WarehouseId above: vehicle number, driver and transporter are already
        // known from the Dispatch Plan the Logistics Manager entered - no reason to leave the job
        // list showing "-" for all of them until a physical gate check-in re-types the same info.
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == vehicleNumber);
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = vehicleNumber };
            _db.Vehicles.Add(vehicle);
        }
        createdTransaction.Vehicle = vehicle;
        createdTransaction.DriverName = matched.Select(r => r.DriverName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        createdTransaction.DriverMobile = matched.Select(r => r.DriverPhone).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
        createdTransaction.TransporterName = matched.Select(r => r.TransporterName).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        foreach (var row in matched)
        {
            row.ConsumedByOutwardTransactionId = createdTransaction.Id;
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(createdTransaction.Id) ?? dto;
    }

    // Matches each row's SkuCode against the Product master (trimmed, case-insensitive - Excel-
    // imported codes can carry incidental whitespace/casing differences). Blocks with a full,
    // itemized list rather than letting generation proceed with unplaceable lines - 3D placement
    // hard-requires a linked Product with real dimensions.
    private async Task<Dictionary<int, Product>> ResolveProductsForDispatchPlanRowsAsync(List<VehicleLogisticsRecord> rows)
    {
        var products = await _db.Products.ToListAsync();
        var blankSkuCode = new List<string>();
        var unmatched = new List<string>();
        var ambiguous = new List<string>();
        var productByRowId = new Dictionary<int, Product>();

        foreach (var row in rows)
        {
            var skuCode = row.SkuCode?.Trim();
            if (string.IsNullOrEmpty(skuCode))
            {
                blankSkuCode.Add(row.Sku);
                continue;
            }

            var candidates = products.Where(p => string.Equals(p.SkuCode?.Trim(), skuCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
            {
                unmatched.Add($"{row.Sku} ({skuCode})");
            }
            else if (candidates.Count > 1)
            {
                ambiguous.Add($"{row.Sku} ({skuCode})");
            }
            else
            {
                productByRowId[row.Id] = candidates[0];
            }
        }

        if (blankSkuCode.Count > 0 || unmatched.Count > 0 || ambiguous.Count > 0)
        {
            var parts = new List<string>();
            if (blankSkuCode.Count > 0) parts.Add($"no SKU code set: {string.Join(", ", blankSkuCode)}");
            if (unmatched.Count > 0) parts.Add($"no matching Product: {string.Join(", ", unmatched)}");
            if (ambiguous.Count > 0) parts.Add($"matches more than one Product: {string.Join(", ", ambiguous)}");
            throw new InvalidOperationException(
                $"Cannot generate pick list - register these SKUs in Admin > Products first ({string.Join("; ", parts)}).");
        }

        return productByRowId;
    }

    public async Task<List<OutwardJobDto>> GetAvailableAsync()
    {
        var transactions = await Query()
            .Where(t => t.Status == OutwardStatus.PickListGenerated)
            .OrderBy(t => t.CreatedTime)
            .ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    public async Task<List<OutwardJobDto>> GetMineAsync(string supervisorUserId)
    {
        var transactions = await Query()
            .Where(t => t.AssignedSupervisorUserId == supervisorUserId && t.Status != OutwardStatus.Completed)
            .OrderBy(t => t.AssignedTime)
            .ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    // No caller passes a filter today without also narrowing by vehicle/order/date in practice,
    // but nothing enforced that - an unfiltered request used to return every completed job this
    // supervisor has ever had, unbounded, growing every day. Capped to the most recent 200 so a
    // busy warehouse's history can't silently balloon a mobile client's payload/parse time.
    private const int MaxUnfilteredHistoryResults = 200;

    public async Task<List<OutwardJobDto>> GetHistoryForSupervisorAsync(
        string supervisorUserId, string? vehicleNumber, string? dispatchOrderNumber, DateTime? date)
    {
        var query = Query().Where(t =>
            t.AssignedSupervisorUserId == supervisorUserId && t.Status == OutwardStatus.Completed);

        var isFiltered = false;
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }
        if (!string.IsNullOrWhiteSpace(dispatchOrderNumber))
        {
            query = query.Where(t => t.DispatchOrder!.DispatchOrderNumber.Contains(dispatchOrderNumber));
            isFiltered = true;
        }
        if (date.HasValue)
        {
            query = query.Where(t => t.DockOutTime.HasValue && t.DockOutTime.Value.Date == date.Value.Date);
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.DockOutTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    public async Task<OutwardJobDto?> GetByIdAsync(int id)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id);
        if (transaction is null)
        {
            return null;
        }

        var progress = await GetLoadPlanProgressAsync(new[] { id });
        return MapToDto(transaction, progress);
    }

    // Runs the real rule-engine (same one the standalone Load Planner uses) against this job's
    // actual dispatch-order lines and real vehicle capacity. Returns null when vehicle capacity
    // isn't on file - the controller turns that into a friendly "not available yet" response,
    // mirroring how the 3D preview already degrades gracefully when dims are unknown.
    // Fields the real Product/Vehicle master data doesn't carry yet (fragility, hazard class,
    // axle ratings, etc.) get honest, permissive defaults - those rules simply stay inert until
    // real master data grows those fields, rather than guessing.
    public async Task<LoadPlanResultDto?> GetLoadPlanAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);
        var vehicle = transaction.Vehicle;

        if (vehicle?.MaxWeightKg is null || vehicle.LengthCm is null || vehicle.WidthCm is null || vehicle.HeightCm is null)
        {
            return null;
        }

        var vehicleProfile = new VehicleProfile
        {
            Name = vehicle.Number,
            Length = (double)vehicle.LengthCm.Value,
            Width = (double)vehicle.WidthCm.Value,
            Height = (double)vehicle.HeightCm.Value,
            MaxPayload = (double)vehicle.MaxWeightKg.Value,
            FrontAxleLimit = double.MaxValue,
            RearAxleLimit = double.MaxValue,
            EmptyWeight = 0
        };

        var items = transaction.DispatchOrder!.Lines.Select(l => new ProductItem
        {
            Sku = l.Id.ToString(),
            Description = l.ProductName,
            Quantity = (int)Math.Max(1, Math.Ceiling((double)l.OrderedQty)),
            Length = (double)(l.Product?.LengthCm ?? 0),
            Width = (double)(l.Product?.WidthCm ?? 0),
            Height = (double)(l.Product?.HeightCm ?? 0),
            Weight = (double)(l.Product?.WeightKg ?? 0)
        }).ToList();

        var result = _loadPlanningEngine.OptimizeExplicit(vehicleProfile, items);
        return LoadPlanningResultMapper.ToDto(result);
    }

    public async Task<OutwardJobDto> ClaimAsync(int id, string supervisorUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.Status != OutwardStatus.PickListGenerated)
        {
            throw new InvalidOperationException("This job has already been claimed.");
        }

        transaction.AssignedSupervisorUserId = supervisorUserId;
        transaction.AssignedTime = DateTime.UtcNow;
        transaction.Status = OutwardStatus.Assigned;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Outward job '{transaction.DispatchOrder?.DispatchOrderNumber}' claimed by supervisor.", supervisorUserId);

        // A job can't have a load plan before it's even been claimed - no progress to look up.
        var dto = MapToDto(transaction, loadPlanTotalSteps: 0, loadPlanResolvedSteps: 0);
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("OutwardJobClaimed", dto);
        return dto;
    }

    // Office's own list uses the identical warehouse-scoping convention as
    // InwardService.GetForOfficeAsync - mirrors the scoping convention DashboardController.GetSummary
    // already established.
    public async Task<List<OutwardJobDto>> GetForOfficeAsync(int warehouseId, bool activeOnly, string? vehicleNumber)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId);

        var isFiltered = activeOnly;
        if (activeOnly)
        {
            query = query.Where(t => t.Status != OutwardStatus.Completed);
        }
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.CreatedTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    public async Task<OutwardJobDto?> GetByIdForOfficeAsync(int id, int warehouseId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id && t.WarehouseId == warehouseId);
        if (transaction is null)
        {
            return null;
        }

        var progress = await GetLoadPlanProgressAsync(new[] { id });
        return MapToDto(transaction, progress);
    }

    // Office-driven assignment, distinct from the supervisor self-claim above (ClaimAsync): it can
    // target any supervisor (not just the caller), is warehouse-guarded, and - unlike ClaimAsync -
    // allows reassigning a job that's already Assigned (a supervisor called in sick, etc.). Mirrors
    // InwardService.AssignSupervisorAsync exactly.
    public async Task<OutwardJobDto> AssignSupervisorAsync(int id, string supervisorUserId, int officeWarehouseId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.WarehouseId != officeWarehouseId)
        {
            throw new UnauthorizedAccessException("This job is not in your warehouse.");
        }

        if (transaction.Status == OutwardStatus.Completed)
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
        if (transaction.Status == OutwardStatus.PickListGenerated)
        {
            transaction.Status = OutwardStatus.Assigned;
        }
        await _db.SaveChangesAsync();

        var dto = await GetByIdAsync(id) ?? throw new InvalidOperationException("Transaction disappeared unexpectedly.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("OutwardJobUpdated", dto);
        // See InwardService.AssignSupervisorAsync's identical targeted send for why - personal
        // "you've been assigned" banner for this one supervisor, on top of the group refresh above.
        await _hub.Clients.User(supervisorUserId).SendAsync("OutwardJobAssignedToYou", dto);
        return dto;
    }

    public async Task<OutwardJobDto> DockInAsync(int id, string supervisorUserId, DockInOutwardRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != OutwardStatus.Assigned)
        {
            throw new InvalidOperationException("Job must be assigned before dock-in.");
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == request.VehicleNumber);
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = request.VehicleNumber };
            _db.Vehicles.Add(vehicle);
        }
        await BackfillVehicleCapacityAsync(vehicle);

        transaction.Vehicle = vehicle;
        transaction.BayName = request.BayName;
        transaction.DockInTime = DateTime.UtcNow;
        transaction.Status = OutwardStatus.Docked;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Vehicle '{request.VehicleNumber}' docked in at {request.BayName}.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<OutwardJobDto> StartLoadingAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != OutwardStatus.Docked)
        {
            throw new InvalidOperationException("Job must be docked before loading can start.");
        }

        transaction.LoadingStartTime = DateTime.UtcNow;
        transaction.Status = OutwardStatus.Loading;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Loading started for vehicle '{transaction.Vehicle?.Number}'.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<OutwardJobDto> AddPhotoAsync(int id, string supervisorUserId, OutwardPhotoType type, string fileName, Stream content)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("Photos can only be added after dock-in and before completion.");
        }

        var filePath = await _photoStorage.SaveAsync($"outward-{id}", fileName, content);

        _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
        {
            OutwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    public async Task<OutwardJobDto> SubmitLoadLinesAsync(int id, string supervisorUserId, SubmitLoadLinesRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("Job must be docked before load lines can be recorded.");
        }

        var validLineIds = transaction.DispatchOrder!.Lines.Select(l => l.Id).ToHashSet();
        if (request.Lines.Count == 0 || request.Lines.Any(l => !validLineIds.Contains(l.DispatchOrderLineId)))
        {
            throw new InvalidOperationException("One or more dispatch order lines are invalid for this transaction.");
        }

        var existing = await _db.OutwardLoadLines.Where(l => l.OutwardTransactionId == id).ToListAsync();
        _db.OutwardLoadLines.RemoveRange(existing);

        foreach (var line in request.Lines)
        {
            _db.OutwardLoadLines.Add(new OutwardLoadLine
            {
                OutwardTransactionId = id,
                DispatchOrderLineId = line.DispatchOrderLineId,
                LoadedQty = line.LoadedQty,
                LoadSequence = line.LoadSequence,
                Notes = line.Notes
            });
        }

        transaction.Status = OutwardStatus.Loading;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Load lines recorded for vehicle '{transaction.Vehicle?.Number}' - {request.Lines.Count} line(s).", supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    public async Task<OutwardJobDto> ReportExceptionAsync(int id, string supervisorUserId, ReportOutwardExceptionRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("Exceptions can only be reported after dock-in and before completion.");
        }

        transaction.ExceptionReason = request.Reason;
        transaction.ExceptionRemarks = request.Remarks;
        transaction.ExceptionReportedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Exception reported: {request.Reason}{(string.IsNullOrWhiteSpace(request.Remarks) ? "" : $" - {request.Remarks}")}.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<OutwardJobDto> ConfirmDispatchReadyAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != OutwardStatus.Loading)
        {
            throw new InvalidOperationException("Job must be in loading before dispatch readiness can be confirmed.");
        }

        transaction.DispatchReadyConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Dispatch readiness confirmed for vehicle '{transaction.Vehicle?.Number}'.", supervisorUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<OutwardJobDto> CompleteAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != OutwardStatus.Loading)
        {
            throw new InvalidOperationException("Job must be in loading before it can be completed.");
        }

        if (transaction.DispatchReadyConfirmedAt is null)
        {
            throw new InvalidOperationException("Confirm dispatch readiness before completing.");
        }

        if (transaction.Photos.Count == 0)
        {
            throw new InvalidOperationException("At least one photo is required before completing.");
        }

        if (transaction.LoadLines.Count == 0)
        {
            throw new InvalidOperationException("Load lines must be recorded before completing.");
        }

        // Additive/optional: jobs that never used the 3D "Plan & Load" flow (no
        // selected option, or a selected option with zero groups) are ungated
        // exactly as before - this only blocks completion when a 3D plan exists
        // and still has unresolved groups.
        if (!await _loadPlanService.AreAllSelectedGroupsResolvedAsync(id))
        {
            throw new InvalidOperationException("All load plan groups must be confirmed (loaded, mismatch, short-load, or skipped) before completing.");
        }

        var isPartial = transaction.LoadLines.Any(l => l.LoadedQty < l.DispatchOrderLine!.OrderedQty);
        var dispatchNoteNumber = $"DN-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

        transaction.DockOutTime = DateTime.UtcNow;
        transaction.Status = OutwardStatus.Completed;

        _db.OutwardDispatchNotes.Add(new OutwardDispatchNote
        {
            OutwardTransactionId = id,
            DispatchNoteNumber = dispatchNoteNumber,
            GeneratedAt = DateTime.UtcNow,
            IsPartial = isPartial
        });

        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Outward completed for vehicle '{transaction.Vehicle?.Number}' - {dispatchNoteNumber}{(isPartial ? " (partial load)" : "")}.",
            supervisorUserId);

        // Deliberately NOT calling _vehicleLogisticsSync.MarkCompletedAsync here - "Completed"
        // here just means loading finished at the source warehouse (the truck hasn't even
        // gated out yet, let alone arrived). Marking the linked Dispatch Plan row Completed
        // this early would hide it from the destination warehouse's "Expected (Not Yet
        // Arrived)" list before the shipment has actually been received - a Dispatch Plan
        // row's journey only really finishes when the INWARD side completes (see
        // InwardService.CompleteAsync), which is the only caller of MarkCompletedAsync now.

        if (isPartial)
        {
            // A real, visible Office to-do (surfaced on the Follow-ups page) - the log line
            // stays as an ops-side trace.
            var shortfallSummary = string.Join("; ", transaction.LoadLines
                .Where(l => l.LoadedQty < l.DispatchOrderLine!.OrderedQty)
                .Select(l => $"{l.DispatchOrderLine!.ProductName}: {l.LoadedQty:0.##} of {l.DispatchOrderLine.OrderedQty:0.##}"));
            _db.FollowUpTasks.Add(new FollowUpTask
            {
                Type = FollowUpType.PartialLoadDispatch,
                EntityName = "OutwardTransaction",
                EntityId = id,
                WarehouseId = transaction.WarehouseId,
                Title = $"Partial load {dispatchNoteNumber} - stock transfer-out note required",
                Details = $"Vehicle {transaction.Vehicle?.Number}, DO {transaction.DispatchOrder?.DispatchOrderNumber}. Short lines - {shortfallSummary}.",
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("FollowUpsChanged");

            _logger.LogWarning("Outward transaction {Id} completed as a partial load; stock transfer-out note required.", id);
        }

        return await BroadcastAndReturn(id);
    }

    // Lets a supervisor reopen a job they marked Completed by mistake, or redo Actual Loading
    // Confirmation for testing - reverts everything CompleteAsync set and clears every group's
    // confirmation status so the Confirm Loading screen starts fresh again. Deliberately leaves
    // photos/exceptions/load lines alone: nothing about those blocks re-confirming groups, and
    // clearing them isn't part of what "restart the loading" means.
    public async Task<OutwardJobDto> RestartLoadingAsync(int id, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status != OutwardStatus.Completed)
        {
            throw new InvalidOperationException("Only a completed job can have its loading restarted.");
        }

        if (transaction.GateOutTime is not null)
        {
            throw new InvalidOperationException("This vehicle has already exited the gate - loading can't be restarted.");
        }

        transaction.Status = OutwardStatus.Loading;
        transaction.DockOutTime = null;
        transaction.DispatchReadyConfirmedAt = null;

        // Unique per transaction - must go before the job can be completed again.
        if (transaction.DispatchNote is not null)
        {
            _db.OutwardDispatchNotes.Remove(transaction.DispatchNote);
        }

        await _db.SaveChangesAsync();
        await _loadPlanService.ResetConfirmationForRestartAsync(id);
        await _vehicleLogisticsSync.MarkInProgressAsync(transaction.Vehicle?.Number, transaction.WarehouseId);

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Loading restarted for vehicle '{transaction.Vehicle?.Number}' - previous dispatch note voided.", supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    public async Task<OutwardJobDto> OutwardCheckInAsync(OutwardGateCheckInRequest request, string securityUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t =>
            t.DispatchOrder!.DispatchOrderNumber == request.DispatchOrderNumber && t.Status != OutwardStatus.Completed)
            ?? throw new InvalidOperationException($"Dispatch order '{request.DispatchOrderNumber}' was not found or is already completed.");

        if (transaction.GateInTime is not null)
        {
            throw new InvalidOperationException("This dispatch order has already been checked in at the gate.");
        }

        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == request.VehicleNumber);
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = request.VehicleNumber };
            _db.Vehicles.Add(vehicle);
        }
        await BackfillVehicleCapacityAsync(vehicle);

        // Attributes the transaction to a warehouse purely from the checking-in Security user's own
        // WarehouseId - no mobile app change needed, the gate-in form never asks for a warehouse.
        transaction.WarehouseId = await _db.Users
            .Where(u => u.Id == securityUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        transaction.Vehicle = vehicle;
        transaction.GateInTime = DateTime.UtcNow;
        transaction.GateInBySecurityUserId = securityUserId;
        transaction.DriverName = request.DriverName;
        transaction.DriverMobile = request.DriverMobile;
        transaction.TransporterName = request.TransporterName;
        transaction.GateName = request.GateName;
        transaction.GpsLatitude = request.GpsLatitude;
        transaction.GpsLongitude = request.GpsLongitude;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", transaction.Id, AuditAction.Updated,
            $"Vehicle '{request.VehicleNumber}' gate-checked-in for dispatch order '{request.DispatchOrderNumber}'{(string.IsNullOrWhiteSpace(request.GateName) ? "" : $" at {request.GateName}")}.",
            securityUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    public async Task<OutwardJobDto> AddGatePhotoAsync(int id, string securityUserId, OutwardPhotoType type, string fileName, Stream content)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.GateInBySecurityUserId != securityUserId)
        {
            throw new UnauthorizedAccessException("This vehicle was not checked in by you.");
        }

        if (transaction.GateOutTime is not null)
        {
            throw new InvalidOperationException("Gate photos can only be added while the vehicle is on-site.");
        }

        var filePath = await _photoStorage.SaveAsync($"outward-{id}", fileName, content);

        _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
        {
            OutwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return await BroadcastAndReturn(id);
    }

    // Warehouse-scoped, matching InwardService.GetForSecurityAsync's convention.
    public async Task<List<OutwardJobDto>> GetForSecurityAsync(int? warehouseId, bool activeOnly, string? vehicleNumber, string? dispatchOrderNumber, DateTime? date)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId);

        var isFiltered = activeOnly;
        if (activeOnly)
        {
            query = query.Where(t => t.Status != OutwardStatus.Completed);
        }
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
            isFiltered = true;
        }
        if (!string.IsNullOrWhiteSpace(dispatchOrderNumber))
        {
            query = query.Where(t => t.DispatchOrder!.DispatchOrderNumber.Contains(dispatchOrderNumber));
            isFiltered = true;
        }
        if (date.HasValue)
        {
            query = query.Where(t => t.GateInTime.HasValue && t.GateInTime.Value.Date == date.Value.Date);
            isFiltered = true;
        }

        var ordered = query.OrderByDescending(t => t.GateInTime);
        var transactions = await (isFiltered ? ordered : ordered.Take(MaxUnfilteredHistoryResults)).ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    public async Task<List<OutwardJobDto>> GetPendingExitAsync(int? warehouseId, string? vehicleNumber)
    {
        var query = Query().Where(t => t.WarehouseId == warehouseId && t.Status == OutwardStatus.Completed && t.GateOutTime == null);
        if (!string.IsNullOrWhiteSpace(vehicleNumber))
        {
            query = query.Where(t => t.Vehicle!.Number.Contains(vehicleNumber));
        }

        var transactions = await query.OrderBy(t => t.DockOutTime).ToListAsync();
        var progress = await GetLoadPlanProgressAsync(transactions.Select(t => t.Id).ToList());
        return transactions.Select(t => MapToDto(t, progress)).ToList();
    }

    public async Task<OutwardJobDto> RecordExitAsync(int id, string securityUserId, int? warehouseId, string fileName, Stream content)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Transaction not found.");

        if (transaction.WarehouseId != warehouseId)
        {
            throw new UnauthorizedAccessException("This vehicle is not in your warehouse.");
        }
        if (transaction.Status != OutwardStatus.Completed)
        {
            throw new InvalidOperationException("Vehicle must complete loading before it can exit.");
        }
        if (transaction.GateOutTime is not null)
        {
            throw new InvalidOperationException("This vehicle has already exited.");
        }

        var filePath = await _photoStorage.SaveAsync($"outward-{id}", fileName, content);
        _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
        {
            OutwardTransactionId = id,
            Type = OutwardPhotoType.VehicleAtExit,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });

        transaction.GateOutTime = DateTime.UtcNow;
        transaction.GateOutBySecurityUserId = securityUserId;
        transaction.GatePassToken = $"EXIT-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Vehicle '{transaction.Vehicle?.Number}' exited the gate - pass {transaction.GatePassToken}.", securityUserId);

        return await BroadcastAndReturn(id);
    }

    // Vehicle Master is now a type/category profile. Transactional vehicles keep their own plate
    // and capacity values; seeded demo plates are backfilled by SeedData.
    private async Task BackfillVehicleCapacityAsync(Vehicle vehicle)
    {
        await Task.CompletedTask;
    }

    private async Task<OutwardTransaction> GetOwnedTransactionAsync(int id, string supervisorUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.AssignedSupervisorUserId != supervisorUserId)
        {
            throw new UnauthorizedAccessException("This job is not assigned to you.");
        }

        return transaction;
    }

    private async Task<OutwardJobDto> BroadcastAndReturn(int id)
    {
        var dto = await GetByIdAsync(id) ?? throw new InvalidOperationException("Transaction disappeared unexpectedly.");
        await _hub.Clients.Groups(InwardHub.SupervisorsGroup, InwardHub.SecurityGroup, InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("OutwardJobUpdated", dto);
        return dto;
    }

    // Lets other services (e.g. OutwardLoadPlanService, whose group-confirmation actions
    // change LoadingCompletionProgress) push a live update without depending on OutwardService
    // for anything else - avoids a circular constructor dependency between the two services.
    public async Task PushUpdateAsync(int id) => await BroadcastAndReturn(id);

    private static OutwardJobDto MapToDto(OutwardTransaction t, IReadOnlyDictionary<int, (int Total, int Resolved)> progress)
    {
        var (total, resolved) = progress.TryGetValue(t.Id, out var p) ? p : (0, 0);
        return MapToDto(t, total, resolved);
    }

    private static OutwardJobDto MapToDto(OutwardTransaction t, int loadPlanTotalSteps, int loadPlanResolvedSteps) => new(
        t.Id,
        t.DispatchOrder!.DispatchOrderNumber,
        t.DispatchOrder.CustomerName,
        t.OutwardTxnNumber,
        t.Status.ToString(),
        t.CreatedTime,
        t.GateInTime,
        t.DriverName,
        t.DriverMobile,
        t.TransporterName,
        t.GateName,
        t.GpsLatitude,
        t.GpsLongitude,
        t.GateOutTime,
        t.GatePassToken,
        t.AssignedSupervisorUserId,
        t.AssignedTime,
        t.Vehicle?.Number,
        t.BayName,
        t.DockInTime,
        t.LoadingStartTime,
        t.DockOutTime,
        t.ExceptionReason?.ToString(),
        t.ExceptionRemarks,
        t.ExceptionReportedAt,
        t.DispatchReadyConfirmedAt,
        t.Vehicle?.MaxWeightKg,
        t.Vehicle?.LengthCm,
        t.Vehicle?.WidthCm,
        t.Vehicle?.HeightCm,
        t.DispatchOrder.Lines.Select(l => new DispatchOrderLineDto(
            l.Id, l.ProductName, l.OrderedQty, l.UnitOfMeasure,
            l.Product?.WeightKg, l.Product?.LengthCm, l.Product?.WidthCm, l.Product?.HeightCm,
            l.Product?.SkuCode, l.Product?.ColorHex, l.DeliveryLocation)).ToList(),
        t.Photos.Select(p => new OutwardPhotoDto(p.Id, p.Type.ToString(), p.FilePath, p.CapturedAt)).ToList(),
        t.LoadLines.Select(l => new LoadLineDto(
            l.Id, l.DispatchOrderLineId, l.DispatchOrderLine!.ProductName, l.DispatchOrderLine.OrderedQty,
            l.LoadedQty, l.LoadSequence, l.Notes)).ToList(),
        t.DispatchNote is null ? null : new OutwardDispatchNoteDto(t.DispatchNote.DispatchNoteNumber, t.DispatchNote.GeneratedAt, t.DispatchNote.IsPartial),
        loadPlanTotalSteps,
        loadPlanResolvedSteps);
}

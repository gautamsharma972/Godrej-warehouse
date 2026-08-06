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
    // the matching VehicleLogisticsRecord group (this PO, From = the caller's own warehouse, still
    // InTransit - PO Number, not Vehicle Number, since a vehicle isn't known this early; Security
    // supplies one independently at gate-in and Office links it afterward via LinkVehicleAsync),
    // synthesizes a DispatchOrder + lines from it (Product-matched by SkuCode - a match isn't
    // required for placement any more, see ResolveProductsForDispatchPlanRowsAsync below, but is
    // still attempted here so linked lines get real master dimensions instead of the placeholder),
    // then hands off to GeneratePickListAsync unchanged.
    public async Task<OutwardJobDto> GeneratePickListFromDispatchPlanAsync(string poNumber, int officeWarehouseId, string officeUserId)
    {
        var matched = await _db.VehicleLogisticsRecords
            .Include(r => r.ToWarehouse)
            .Where(r => r.PoNumber == poNumber && r.FromWarehouseId == officeWarehouseId
                && r.Status == VehicleLogisticsStatus.InTransit)
            .ToListAsync();

        if (matched.Count == 0)
        {
            throw new InvalidOperationException($"No pending Dispatch Plan rows found for PO '{poNumber}'.");
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
                $"Dispatch Plan rows for PO '{poNumber}' are already being processed by another request.");
        }

        // Lets the Logistics Manager's own Dispatch Plan list and Office's pending panels drop this
        // group live the moment it's claimed, instead of waiting for their next unrelated refresh.
        await _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

        var toWarehouseName = matched[0].ToWarehouse!.Name;
        var dispatchOrder = new DispatchOrder
        {
            DispatchOrderNumber = $"DP-{DateTime.UtcNow:yyyyMMdd}-{poNumber}-{DateTime.UtcNow.Ticks % 10000}",
            CustomerName = toWarehouseName,
            RequestedDate = matched.Any(r => r.EtaDateTime.HasValue)
                ? matched.Where(r => r.EtaDateTime.HasValue).Min(r => r.EtaDateTime!.Value)
                : DateTime.UtcNow,
            Lines = matched.Select(r => new DispatchOrderLine
            {
                ProductName = r.Sku,
                OrderedQty = r.PickListQuantity ?? r.BoxQuantity,
                UnitOfMeasure = "PCS",
                ProductId = productByRowId.TryGetValue(r.Id, out var product) ? product.Id : null,
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

        // Same reasoning as WarehouseId above: if the Dispatch Plan rows already carry a vehicle
        // number (legacy/manual-entry rows - no longer required upstream), driver and transporter are
        // already known too - no reason to leave the job list showing "-" for all of them until a
        // physical gate check-in re-types the same info. The normal case now is no vehicle number yet
        // at all - the job stays unassigned until Office's Link Vehicle action (LinkVehicleAsync)
        // attaches a Security gate arrival to it.
        var vehicleNumber = matched.Select(r => r.VehicleNumber).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        if (vehicleNumber is not null)
        {
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
        }

        foreach (var row in matched)
        {
            row.ConsumedByOutwardTransactionId = createdTransaction.Id;
        }
        await _db.SaveChangesAsync();

        return await GetByIdAsync(createdTransaction.Id) ?? dto;
    }

    // Matches each row's SkuCode against the Product master (trimmed, case-insensitive - Excel-
    // imported codes can carry incidental whitespace/casing differences). No longer blocks pick-list
    // generation on a miss (Office generating the pick list shouldn't be held hostage to Admin
    // having registered every SKU yet) - a row with no unique match just gets ProductId == null on
    // its DispatchOrderLine. The 3D load planner tolerates that too (OutwardLoadPlanService.
    // BuildUnitProduct falls back to a 1cm placeholder per missing dimension) so placement isn't
    // blocked, it just renders/stacks as a nominal 1x1x1cm cube until the SKU gets Product-linked
    // with real master dimensions.
    private async Task<Dictionary<int, Product>> ResolveProductsForDispatchPlanRowsAsync(List<VehicleLogisticsRecord> rows)
    {
        var products = await _db.Products.ToListAsync();
        var productByRowId = new Dictionary<int, Product>();

        foreach (var row in rows)
        {
            var skuCode = row.SkuCode?.Trim();
            if (string.IsNullOrEmpty(skuCode))
            {
                continue;
            }

            var candidates = products.Where(p => string.Equals(p.SkuCode?.Trim(), skuCode, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1)
            {
                productByRowId[row.Id] = candidates[0];
            }
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
        var dto = MapToDto(transaction, progress);
        return dto with { MismatchReceiptsAtDestination = await ResolveMismatchReceiptsAsync(id) };
    }

    // Bridges back from this Outward job to whatever it was received against at the destination -
    // via the same VehicleLogisticsRecord rows InwardService uses for the reverse direction
    // (ConsumedByOutwardTransactionId/ConsumedByInwardTransactionId) - and surfaces any SKU the
    // destination discovered only during receiving inspection ("Mismatch SKU Details" /
    // UnplannedReceiptLine), never expected/loaded here at all, so the source warehouse's Office
    // can see the full picture of what actually arrived, not just what was dispatched.
    private async Task<List<UnplannedReceiptAtDestinationDto>> ResolveMismatchReceiptsAsync(int outwardTransactionId)
    {
        var inwardTransactionId = await _db.VehicleLogisticsRecords
            .Where(r => r.ConsumedByOutwardTransactionId == outwardTransactionId && r.ConsumedByInwardTransactionId != null)
            .Select(r => r.ConsumedByInwardTransactionId!.Value)
            .Distinct()
            .FirstOrDefaultAsync();

        if (inwardTransactionId == 0)
        {
            return new List<UnplannedReceiptAtDestinationDto>();
        }

        return await _db.UnplannedReceiptLines
            .Include(l => l.Product)
            .Where(l => l.InwardTransactionId == inwardTransactionId)
            .Select(l => new UnplannedReceiptAtDestinationDto(l.Product!.Name, l.Product.SkuCode, l.Quantity, l.Notes))
            .ToListAsync();
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

        // No real capacity ceiling is enforced here - this is a convenience simulation for where
        // cartons would physically sit, not a hard loading rule (mirrors
        // OutwardLoadPlanService.GetVehicleProfile's identical fallback for the 3D workspace).
        var vehicleProfile = new VehicleProfile
        {
            Name = vehicle?.Number ?? "Unknown",
            Length = (double?)vehicle?.LengthCm ?? 2000,
            Width = (double?)vehicle?.WidthCm ?? 300,
            Height = (double?)vehicle?.HeightCm ?? 300,
            MaxPayload = (double?)vehicle?.MaxWeightKg ?? 100000,
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
        var dto = MapToDto(transaction, progress);
        return dto with { MismatchReceiptsAtDestination = await ResolveMismatchReceiptsAsync(id) };
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

    private const int MaxSkuPhotosPerLine = 2;
    private const int MaxSkuPhotosPerJob = 10;

    public async Task<OutwardJobDto> AddPhotoAsync(int id, string supervisorUserId, OutwardPhotoType type, string fileName, Stream content, int? dispatchOrderLineId = null)
    {
        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("Photos can only be added after dock-in and before completion.");
        }

        if (dispatchOrderLineId is int lineId)
        {
            if (transaction.DispatchOrder!.Lines.All(l => l.Id != lineId))
            {
                throw new InvalidOperationException("This dispatch order line is invalid for this transaction.");
            }

            var existingForLine = transaction.Photos.Count(p => p.Type == OutwardPhotoType.SkuLoaded && p.DispatchOrderLineId == lineId);
            if (existingForLine >= MaxSkuPhotosPerLine)
            {
                throw new InvalidOperationException($"Only {MaxSkuPhotosPerLine} photos are allowed per SKU.");
            }

            var existingForJob = transaction.Photos.Count(p => p.Type == OutwardPhotoType.SkuLoaded);
            if (existingForJob >= MaxSkuPhotosPerJob)
            {
                throw new InvalidOperationException($"Only {MaxSkuPhotosPerJob} SKU photos are allowed per job.");
            }
        }

        var filePath = await _photoStorage.SaveAsync($"outward-{id}", fileName, content);

        _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
        {
            OutwardTransactionId = id,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow,
            DispatchOrderLineId = dispatchOrderLineId
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

    // Lets a Supervisor load extra stock beyond the original pick list, once Plan & Load shows
    // there's still spare vehicle space - adds a new DispatchOrderLine (Product-linked, matching
    // GeneratePickListFromDispatchPlanAsync's own lines, so the 3D engine can place it) that the
    // workspace picks up on its next refresh, same as any originally-planned SKU.
    public async Task<OutwardJobDto> AddDispatchOrderLineAsync(int id, string supervisorUserId, AddDispatchOrderLineRequest request)
    {
        if (request.Quantity <= 0)
        {
            throw new InvalidOperationException("Quantity must be greater than zero.");
        }

        var transaction = await GetOwnedTransactionAsync(id, supervisorUserId);

        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("SKUs can only be added after dock-in and before completion.");
        }

        var product = await _db.Products.FindAsync(request.ProductId)
            ?? throw new InvalidOperationException("Product not found.");

        var deliveryLocation = transaction.DispatchOrder!.Lines
            .Select(l => l.DeliveryLocation)
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;

        var line = new DispatchOrderLine
        {
            DispatchOrderId = transaction.DispatchOrderId,
            ProductId = product.Id,
            ProductName = product.Name,
            OrderedQty = request.Quantity,
            UnitOfMeasure = "PCS",
            DeliveryLocation = deliveryLocation,
            IsExtra = true
        };
        _db.DispatchOrderLines.Add(line);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", transaction.Id, AuditAction.Updated,
            $"SKU '{product.Name}' ({request.Quantity} {line.UnitOfMeasure}) added to dispatch order '{transaction.DispatchOrder.DispatchOrderNumber}' by Supervisor.",
            supervisorUserId);

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

        transaction.DockOutTime = DateTime.UtcNow;
        transaction.Status = OutwardStatus.PendingOfficeVerification;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
            $"Loading completed for vehicle '{transaction.Vehicle?.Number}' - pending Office verification.",
            supervisorUserId);

        return await BroadcastAndReturn(id);
    }

    // Office's completion gate: reviews Ordered/Pick List/Loaded quantities (the web
    // OutwardJobDetail page's own DO Lines table) and confirms - only then is the
    // OutwardDispatchNote generated and the job actually finalized. Mirrors
    // InwardService.VerifyAndGenerateGrnAsync exactly (Query(), not GetOwnedTransactionAsync -
    // Office isn't the assigned supervisor).
    public async Task<OutwardJobDto> VerifyAndCompleteAsync(int id, string officeUserId)
    {
        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.Status != OutwardStatus.PendingOfficeVerification)
        {
            throw new InvalidOperationException("Job must be pending Office verification before it can be completed.");
        }

        var isPartial = transaction.LoadLines.Any(l => l.LoadedQty < l.DispatchOrderLine!.OrderedQty);
        var dispatchNoteNumber = $"DN-{DateTime.UtcNow:yyyyMMdd}-{id:D4}";

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
            $"Outward verified and completed by Office for vehicle '{transaction.Vehicle?.Number}' - {dispatchNoteNumber}{(isPartial ? " (partial load)" : "")}.",
            officeUserId);

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
                OrganizationId = transaction.OrganizationId,
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

    // Security's outward gate-in, independent of any pick list (mirrors InwardService.CheckInAsync):
    // always creates a new OutwardGateArrival, no matching against an existing OutwardTransaction/
    // DispatchOrder. Office links it to a real pick list afterward (see LinkVehicleAsync below).
    public async Task<OutwardGateArrivalDto> CreateGateArrivalAsync(OutwardGateArrivalCheckInRequest request, string securityUserId)
    {
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Number == request.VehicleNumber);
        if (vehicle is null)
        {
            vehicle = new Vehicle { Number = request.VehicleNumber };
            _db.Vehicles.Add(vehicle);
        }
        await BackfillVehicleCapacityAsync(vehicle);

        // Attributes the arrival to a warehouse purely from the checking-in Security user's own
        // WarehouseId - no mobile app change needed, the gate-in form never asks for a warehouse.
        var warehouseId = await _db.Users
            .Where(u => u.Id == securityUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        var arrival = new OutwardGateArrival
        {
            Vehicle = vehicle,
            WarehouseId = warehouseId,
            GateInTime = DateTime.UtcNow,
            GateInBySecurityUserId = securityUserId,
            DriverName = request.DriverName,
            DriverMobile = request.DriverMobile,
            TransporterName = request.TransporterName,
            GateName = request.GateName,
            GpsLatitude = request.GpsLatitude,
            GpsLongitude = request.GpsLongitude,
            SecurityEnteredDispatchOrderNumber = string.IsNullOrWhiteSpace(request.DispatchOrderNumber) ? null : request.DispatchOrderNumber.Trim()
        };
        _db.OutwardGateArrivals.Add(arrival);
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardGateArrival", arrival.Id, AuditAction.Created,
            $"Vehicle '{request.VehicleNumber}' gate-checked-in for outward" +
            (string.IsNullOrWhiteSpace(request.DispatchOrderNumber) ? "" : $" (DO noted: '{request.DispatchOrderNumber}')") +
            (string.IsNullOrWhiteSpace(request.GateName) ? "" : $" at {request.GateName}") +
            " - not yet linked to a pick list.",
            securityUserId);

        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("OutwardGateArrivalChanged");
        return await GetGateArrivalDtoAsync(arrival.Id);
    }

    // Per-category caps: VehicleAtGate allows up to 5, Driver/VehicleRc/DrivingLicense are id
    // documents so capped tighter - all other OutwardPhotoType values are unrelated to gate
    // arrivals (loading/exit evidence) and stay unlimited here.
    private static int MaxGateArrivalPhotos(OutwardPhotoType type) => type switch
    {
        OutwardPhotoType.VehicleAtGate => 5,
        OutwardPhotoType.Driver => 3,
        OutwardPhotoType.VehicleRc => 2,
        OutwardPhotoType.DrivingLicense => 2,
        _ => int.MaxValue
    };

    public async Task<OutwardGateArrivalDto> AddGateArrivalPhotoAsync(int arrivalId, string securityUserId, OutwardPhotoType type, string fileName, Stream content)
    {
        var arrival = await _db.OutwardGateArrivals.Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.Id == arrivalId)
            ?? throw new KeyNotFoundException("Gate arrival not found.");

        if (arrival.GateInBySecurityUserId != securityUserId)
        {
            throw new UnauthorizedAccessException("This vehicle was not checked in by you.");
        }

        if (arrival.LinkedOutwardTransactionId is not null)
        {
            throw new InvalidOperationException("This vehicle has already been linked to a dispatch order - photos can no longer be added here.");
        }

        var max = MaxGateArrivalPhotos(type);
        if (arrival.Photos.Count(p => p.Type == type) >= max)
        {
            throw new InvalidOperationException($"A maximum of {max} '{type}' photo(s) can be captured.");
        }

        var filePath = await _photoStorage.SaveAsync($"outward-gate-arrival-{arrivalId}", fileName, content);

        _db.OutwardGateArrivalPhotos.Add(new OutwardGateArrivalPhoto
        {
            OutwardGateArrivalId = arrivalId,
            Type = type,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("OutwardGateArrivalChanged");
        return await GetGateArrivalDtoAsync(arrivalId);
    }

    // Office's "Link Vehicle" action: attaches an already-gated-in (Security-created) outward
    // arrival to a pick list Office already generated, picked from the Outward Jobs list - mirrors
    // InwardService.LinkVehicleAsync. The arrival's vehicle/driver/photos move onto the transaction;
    // the arrival itself is kept, just stamped as linked (audit trail, same as VehicleLogisticsRecord's
    // ConsumedBy* columns). Also doubles as "Reassign Vehicle" - callable again on a job that already
    // has a vehicle (e.g. the wrong one was linked, or it needs swapping to one with known capacity
    // for the load-plan simulation) - the previously linked arrival is freed back up in that case.
    public async Task<OutwardJobDto> LinkVehicleAsync(LinkOutwardVehicleRequest request, string officeUserId)
    {
        var warehouseId = await _db.Users
            .Where(u => u.Id == officeUserId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();
        if (warehouseId is null)
        {
            throw new InvalidOperationException("Your account is not assigned to a warehouse.");
        }

        var transaction = await Query().FirstOrDefaultAsync(t => t.Id == request.OutwardTransactionId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.WarehouseId != warehouseId)
        {
            throw new UnauthorizedAccessException("This job is not in your warehouse.");
        }

        if (transaction.Status == OutwardStatus.Completed)
        {
            throw new InvalidOperationException("This job is already completed - its vehicle can no longer be changed.");
        }

        var arrival = await _db.OutwardGateArrivals.Include(a => a.Vehicle).Include(a => a.Photos)
            .FirstOrDefaultAsync(a => a.Id == request.OutwardGateArrivalId)
            ?? throw new KeyNotFoundException("Gate arrival not found.");

        if (arrival.WarehouseId != warehouseId)
        {
            throw new UnauthorizedAccessException("This gate arrival is not in your warehouse.");
        }

        if (arrival.LinkedOutwardTransactionId is not null)
        {
            throw new InvalidOperationException("This vehicle is already linked to a dispatch order.");
        }

        // Reassignment (job already had a vehicle) - free up whichever arrival it was previously
        // linked to, so that vehicle becomes available to link elsewhere again. Nothing to do if the
        // previous vehicle came straight from the Dispatch Plan at pick-list time instead of a real
        // gate arrival (no arrival row exists for it).
        if (transaction.VehicleId is not null)
        {
            var previousArrival = await _db.OutwardGateArrivals
                .FirstOrDefaultAsync(a => a.LinkedOutwardTransactionId == transaction.Id);
            if (previousArrival is not null)
            {
                previousArrival.LinkedOutwardTransactionId = null;
                previousArrival.LinkedAtUtc = null;
                previousArrival.LinkedByOfficeUserId = null;
            }
        }

        transaction.Vehicle = arrival.Vehicle;
        transaction.GateInTime = arrival.GateInTime;
        transaction.GateInBySecurityUserId = arrival.GateInBySecurityUserId;
        transaction.DriverName = arrival.DriverName;
        transaction.DriverMobile = arrival.DriverMobile;
        transaction.TransporterName = arrival.TransporterName;
        transaction.GateName = arrival.GateName;
        transaction.GpsLatitude = arrival.GpsLatitude;
        transaction.GpsLongitude = arrival.GpsLongitude;

        foreach (var photo in arrival.Photos)
        {
            _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
            {
                OutwardTransactionId = transaction.Id,
                Type = photo.Type,
                FilePath = photo.FilePath,
                CapturedAt = photo.CapturedAt
            });
        }

        arrival.LinkedOutwardTransactionId = transaction.Id;
        arrival.LinkedAtUtc = DateTime.UtcNow;
        arrival.LinkedByOfficeUserId = officeUserId;
        await _db.SaveChangesAsync();

        await _audit.LogAsync("OutwardTransaction", transaction.Id, AuditAction.Updated,
            $"Vehicle '{arrival.Vehicle!.Number}' linked/reassigned by Office to dispatch order '{transaction.DispatchOrder?.DispatchOrderNumber}'.", officeUserId);

        return await BroadcastAndReturn(transaction.Id);
    }

    // Backs the vehicle dropdown on Office's Link Vehicle dialog - outward arrivals Security has
    // already gated in at this warehouse but that aren't linked to any pick list yet.
    public async Task<List<OutwardGateArrivalDto>> GetUnlinkedArrivalsAsync(int warehouseId)
    {
        var arrivals = await _db.OutwardGateArrivals
            .Include(a => a.Vehicle)
            .Include(a => a.Photos)
            .Where(a => a.WarehouseId == warehouseId && a.LinkedOutwardTransactionId == null)
            .OrderByDescending(a => a.GateInTime)
            .ToListAsync();

        return arrivals.Select(ToGateArrivalDto).ToList();
    }

    private async Task<OutwardGateArrivalDto> GetGateArrivalDtoAsync(int id)
    {
        var arrival = await _db.OutwardGateArrivals
            .Include(a => a.Vehicle)
            .Include(a => a.Photos)
            .FirstAsync(a => a.Id == id);
        return ToGateArrivalDto(arrival);
    }

    private static OutwardGateArrivalDto ToGateArrivalDto(OutwardGateArrival a) => new(
        a.Id, a.Vehicle!.Number, a.DriverName, a.DriverMobile, a.TransporterName, a.GateName,
        a.GpsLatitude, a.GpsLongitude, a.GateInTime, a.SecurityEnteredDispatchOrderNumber,
        a.LinkedOutwardTransactionId is not null,
        a.Photos.Select(p => new OutwardGateArrivalPhotoDto(p.Id, p.Type.ToString(), p.FilePath, p.CapturedAt)).ToList());

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

    // Vehicle Master is now a type/category profile, not a per-plate capacity source - a plate
    // auto-created at Security's Outward gate-in/Supervisor's Dock-In just gets Vehicle's own
    // generic defaults for whichever fields are still missing (mirrors
    // InwardService.CheckInAsync's identical backfill on the Inward side), so it never sits
    // blocked at "Missing" capacity in the Vehicle Registry by accident.
    private async Task BackfillVehicleCapacityAsync(Vehicle vehicle)
    {
        vehicle.MaxWeightKg ??= Vehicle.DefaultMaxWeightKg;
        vehicle.LengthCm ??= Vehicle.DefaultLengthCm;
        vehicle.WidthCm ??= Vehicle.DefaultWidthCm;
        vehicle.HeightCm ??= Vehicle.DefaultHeightCm;
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
        t.Photos.Select(p => new OutwardPhotoDto(p.Id, p.Type.ToString(), p.FilePath, p.CapturedAt, p.DispatchOrderLineId)).ToList(),
        t.LoadLines.Select(l => new LoadLineDto(
            l.Id, l.DispatchOrderLineId, l.DispatchOrderLine!.ProductName, l.DispatchOrderLine.OrderedQty,
            l.LoadedQty, l.LoadSequence, l.Notes)).ToList(),
        t.DispatchNote is null ? null : new OutwardDispatchNoteDto(t.DispatchNote.DispatchNoteNumber, t.DispatchNote.GeneratedAt, t.DispatchNote.IsPartial),
        loadPlanTotalSteps,
        loadPlanResolvedSteps);
}

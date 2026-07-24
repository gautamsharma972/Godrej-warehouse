using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;
using WarehouseGate.Infrastructure.Storage;
using WarehouseGate.LoadPlanning.Models;
using WarehouseGate.LoadPlanning.Validation;

namespace WarehouseGate.Api.Services;

// Everything behind the 3D "Plan & Load" flow: fixed-zone group placement (6 sections -
// Front/Middle/Back x Left/Right, see OutwardLoadPlanGroup for the axis convention), saved
// Option A/B/C arrangements, on-demand rule warnings, and per-group Actual Loading
// Confirmation. Kept separate from the already-large OutwardService; the only thing
// OutwardService needs from here is AreAllSelectedGroupsResolvedAsync for its Complete gating.
//
// Placement is deliberately NOT a bin-packing/collision engine: a supervisor picks a zone and
// a quantity, groups already in that zone stack additively on top of each other (bookkeeping,
// not geometry), and the only hard rule enforced is that a SKU's total placed quantity across
// all its zones never exceeds its pick-list quantity (DispatchOrderLine.OrderedQty) plus the
// vehicle's weight payload cap. Whether cartons literally fit a zone's footprint is not
// validated - the resulting rows/columns/layers grid is a cosmetic visual only.
public class OutwardLoadPlanService
{
    private const int MaxOptionsPerTransaction = 3;

    private static readonly string[] Palette =
        { "#4f7cff", "#ff9f43", "#26c281", "#e0568c", "#8e6cff", "#ffcf44", "#3fbfbf", "#ff6b6b" };

    private readonly WarehouseGateDbContext _db;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IHubContext<InwardHub> _hub;

    public OutwardLoadPlanService(WarehouseGateDbContext db, IPhotoStorageService photoStorage, IHubContext<InwardHub> hub)
    {
        _db = db;
        _photoStorage = photoStorage;
        _hub = hub;
    }

    // Deliberately a lightweight int payload, not the typed OutwardJob DTO - mobile's
    // OutwardHub client binds "OutwardJobUpdated" as On<OutwardJob> and would break on anything
    // else, so this fires under its own event name. Office/Logistics/Admins refetch the load
    // plan when they see this; there's no supervisor group here since the supervisor editing the
    // plan already has it live in memory.
    private Task BroadcastLoadPlanChangedAsync(int transactionId) =>
        _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup, InwardHub.LogisticsGroup).SendAsync("LoadPlanChanged", transactionId);

    // ---------- Options ----------

    public async Task<List<LoadPlanOptionSummaryDto>> GetOptionsAsync(int transactionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var vehicle = RequireVehicleProfile(transaction);

        return transaction.LoadPlanOptions.OrderBy(o => o.CreatedAt).Select(option =>
        {
            var validation = LoadPlanValidator.Validate(BuildPlacedItems(option.Groups), vehicle);
            return new LoadPlanOptionSummaryDto(
                option.Id, option.Label, option.IsSelected, option.CreatedAt, option.Groups.Count,
                validation.Warnings.Count, ToDto(validation.Simulation, vehicle));
        }).ToList();
    }

    public async Task<LoadPlanOptionSummaryDto> CreateOptionAsync(int transactionId, string supervisorUserId, CreateLoadPlanOptionRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        RequireEditableStatus(transaction);
        var vehicle = RequireVehicleProfile(transaction);

        if (transaction.LoadPlanOptions.Count >= MaxOptionsPerTransaction)
        {
            throw new InvalidOperationException($"A maximum of {MaxOptionsPerTransaction} saved options is allowed per job.");
        }

        var label = string.IsNullOrWhiteSpace(request.Label)
            ? $"Option {(char)('A' + transaction.LoadPlanOptions.Count)}"
            : request.Label.Trim();

        var option = new OutwardLoadPlanOption
        {
            OutwardTransactionId = transactionId,
            Label = label,
            IsSelected = transaction.LoadPlanOptions.Count == 0,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = supervisorUserId
        };

        _db.OutwardLoadPlanOptions.Add(option);
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        var simulation = LoadPlanValidator.Validate(Array.Empty<PlacedItem>(), vehicle).Simulation;
        return new LoadPlanOptionSummaryDto(option.Id, option.Label, option.IsSelected, option.CreatedAt, 0, 0, ToDto(simulation, vehicle));
    }

    public async Task DeleteOptionAsync(int transactionId, int optionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);

        if (option.IsSelected && option.Groups.Any(g => g.ConfirmationStatus != LoadGroupConfirmationStatus.NotStarted))
        {
            throw new InvalidOperationException("Cannot delete the selected option once loading confirmation has started.");
        }

        _db.OutwardLoadPlanGroups.RemoveRange(option.Groups);
        _db.OutwardLoadPlanOptions.Remove(option);
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);
    }

    public async Task<LoadPlanOptionSummaryDto> SelectOptionAsync(int transactionId, int optionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var target = RequireOption(transaction, optionId);
        var vehicle = RequireVehicleProfile(transaction);

        var currentlySelected = transaction.LoadPlanOptions.FirstOrDefault(o => o.IsSelected);
        if (currentlySelected is not null && currentlySelected.Id != optionId
            && currentlySelected.Groups.Any(g => g.ConfirmationStatus != LoadGroupConfirmationStatus.NotStarted))
        {
            throw new InvalidOperationException("Cannot switch away from the current option once loading confirmation has started.");
        }

        foreach (var option in transaction.LoadPlanOptions)
        {
            option.IsSelected = option.Id == optionId;
        }
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        var validation = LoadPlanValidator.Validate(BuildPlacedItems(target.Groups), vehicle);
        return new LoadPlanOptionSummaryDto(
            target.Id, target.Label, true, target.CreatedAt, target.Groups.Count,
            validation.Warnings.Count, ToDto(validation.Simulation, vehicle));
    }

    // ---------- Groups ----------

    public async Task<List<LoadPlanGroupDto>> GetGroupsAsync(int transactionId, int optionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        return option.Groups.OrderBy(g => g.LoadSequence).Select(ToDto).ToList();
    }

    // Bypasses the normal ownership check (GetOwnedTransactionAsync) - deliberately: the caller
    // here is a DIFFERENT supervisor than the one who dispatched this shipment (the one receiving
    // it at the destination warehouse), reached only through InwardService's own cross-reference
    // endpoint, which independently verifies the caller owns the INWARD job before ever calling
    // this. See InwardOutwardReferenceDto for why this exists. Null selected option (never
    // calculated/saved a load plan) just means an empty group list, not an error.
    public async Task<(OutwardTransaction Transaction, List<LoadPlanGroupDto> Groups)?> GetForInwardReferenceAsync(int transactionId)
    {
        var transaction = await _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.DispatchOrder)
            .Include(t => t.LoadPlanOptions).ThenInclude(o => o.Groups).ThenInclude(g => g.DispatchOrderLine)
            .Include(t => t.LoadPlanOptions).ThenInclude(o => o.Groups).ThenInclude(g => g.Photos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == transactionId);

        if (transaction is null)
        {
            return null;
        }

        var option = transaction.LoadPlanOptions.FirstOrDefault(o => o.IsSelected);
        var groups = option is null ? new List<LoadPlanGroupDto>() : option.Groups.OrderBy(g => g.LoadSequence).Select(ToDto).ToList();
        return (transaction, groups);
    }

    // Delivery Unloading View: find where a SKU ended up without the supervisor needing to
    // remember which zone they put it in. Matches by product name, SKU master code, or the
    // dispatch line's delivery location/stop.
    public async Task<List<LoadGroupSearchResultDto>> SearchGroupsAsync(int transactionId, int optionId, string supervisorUserId, string query)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);

        var trimmed = query?.Trim() ?? string.Empty;
        var matches = trimmed.Length == 0
            ? option.Groups
            : option.Groups.Where(g =>
                (g.DispatchOrderLine?.ProductName ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (g.DispatchOrderLine?.Product?.SkuCode ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                (g.DispatchOrderLine?.DeliveryLocation ?? string.Empty).Contains(trimmed, StringComparison.OrdinalIgnoreCase));

        return matches.OrderBy(g => g.LoadSequence).Select(g => new LoadGroupSearchResultDto(
            g.Id,
            g.DispatchOrderLine?.ProductName ?? "Item",
            g.DispatchOrderLine?.Product?.SkuCode ?? string.Empty,
            g.DispatchOrderLine?.DeliveryLocation ?? string.Empty,
            g.Quantity,
            ZoneCode(g),
            HumanReadablePosition(g),
            (double)g.PositionXCm, (double)g.PositionYCm, (double)g.PositionZCm,
            (double)g.DimXCm, (double)g.DimYCm, (double)g.DimZCm)).ToList();
    }

    private static string HumanReadablePosition(OutwardLoadPlanGroup g)
    {
        var length = g.ZoneLength switch
        {
            LoadZoneLength.Front => "front",
            LoadZoneLength.Back => "back",
            _ => "middle"
        };
        var width = g.ZoneWidth switch
        {
            LoadZoneWidth.Left => "left",
            _ => "right"
        };
        var height = g.ZoneHeight switch
        {
            LoadZoneHeight.Bottom => "floor level",
            LoadZoneHeight.Top => "upper level",
            _ => "mid level"
        };
        return $"Near {length}-{width}, {height}";
    }

    public async Task<LoadGroupPreviewDto> PreviewGroupAsync(int transactionId, int optionId, string supervisorUserId, PlaceLoadGroupInZoneRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        var vehicle = RequireVehicleProfile(transaction);

        var (zoneLength, zoneWidth, unitProduct, line) = ResolveZoneRequest(transaction, request);

        var alreadyPlaced = option.Groups
            .Where(g => g.DispatchOrderLineId == request.DispatchOrderLineId && g.Id != request.ExcludeGroupId)
            .Sum(g => g.Quantity);
        var overflow = Math.Max(0, alreadyPlaced + request.Quantity - (int)line.OrderedQty);
        var warnings = new List<string>();
        if (overflow > 0)
        {
            warnings.Add($"Exceeds pick-list quantity by {overflow} - {alreadyPlaced} already placed elsewhere, pick-list is {line.OrderedQty:0.#}.");
        }

        var (originX, originZ, zoneWidthCm, zoneLengthCm) = ZoneFootprint(zoneLength, zoneWidth, vehicle);
        var grid = ComputeZoneGrid(zoneWidthCm, zoneLengthCm, unitProduct, request.Quantity);
        var stackedY = option.Groups
            .Where(g => g.ZoneLength == zoneLength && g.ZoneWidth == zoneWidth && g.Id != request.ExcludeGroupId)
            .Sum(g => (double)g.DimYCm);

        return new LoadGroupPreviewDto(
            grid.Rows, grid.Columns, grid.Layers, request.Quantity, overflow,
            originX, stackedY, originZ,
            grid.DimX, grid.DimY, grid.DimZ,
            overflow == 0, warnings);
    }

    public async Task<LoadPlanGroupDto> CreateGroupAsync(int transactionId, int optionId, string supervisorUserId, PlaceLoadGroupInZoneRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var vehicle = RequireVehicleProfile(transaction);

        var (zoneLength, zoneWidth, unitProduct, line) = ResolveZoneRequest(transaction, request);
        EnsureOrderedQtyNotExceeded(option, request.DispatchOrderLineId, excludeGroupId: null, request.Quantity, line);
        EnsurePayloadNotExceeded(option, vehicle, excludeGroupId: null, unitProduct.Weight * request.Quantity);

        var group = new OutwardLoadPlanGroup
        {
            OutwardLoadPlanOptionId = optionId,
            DispatchOrderLineId = request.DispatchOrderLineId,
            LoadSequence = option.Groups.Count == 0 ? 1 : option.Groups.Max(g => g.LoadSequence) + 1,
            Color = ColorForLine(request.DispatchOrderLineId, transaction),
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = supervisorUserId
        };
        ApplyZoneArrangement(group, zoneLength, zoneWidth, vehicle, unitProduct, request.Quantity);

        _db.OutwardLoadPlanGroups.Add(group);
        RestackZone(option, zoneLength, zoneWidth, vehicle);

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        group.DispatchOrderLine = line;
        return ToDto(group);
    }

    public async Task<LoadPlanGroupDto> UpdateGroupAsync(int transactionId, int optionId, int groupId, string supervisorUserId, PlaceLoadGroupInZoneRequest request)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var group = RequireGroup(option, groupId);
        RequireNotStarted(group);
        RequireNotLocked(group);
        var vehicle = RequireVehicleProfile(transaction);

        var (zoneLength, zoneWidth, unitProduct, line) = ResolveZoneRequest(transaction, request);
        EnsureOrderedQtyNotExceeded(option, request.DispatchOrderLineId, excludeGroupId: groupId, request.Quantity, line);
        EnsurePayloadNotExceeded(option, vehicle, groupId, unitProduct.Weight * request.Quantity);

        var previousZoneLength = group.ZoneLength;
        var previousZoneWidth = group.ZoneWidth;

        group.DispatchOrderLineId = request.DispatchOrderLineId;
        group.Color = ColorForLine(request.DispatchOrderLineId, transaction);
        ApplyZoneArrangement(group, zoneLength, zoneWidth, vehicle, unitProduct, request.Quantity);
        group.UpdatedAt = DateTime.UtcNow;

        if (previousZoneLength != zoneLength || previousZoneWidth != zoneWidth)
        {
            RestackZone(option, previousZoneLength, previousZoneWidth, vehicle);
        }
        RestackZone(option, zoneLength, zoneWidth, vehicle);

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        group.DispatchOrderLine = line;
        return ToDto(group);
    }

    public async Task DeleteGroupAsync(int transactionId, int optionId, int groupId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        var group = RequireGroup(option, groupId);
        RequireNotStarted(group);
        RequireNotLocked(group);
        var vehicle = RequireVehicleProfile(transaction);

        var zoneLength = group.ZoneLength;
        var zoneWidth = group.ZoneWidth;

        option.Groups.Remove(group);
        _db.OutwardLoadPlanGroups.Remove(group);

        RestackZone(option, zoneLength, zoneWidth, vehicle);

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);
    }

    public async Task<LoadPlanGroupDto> DuplicateGroupAsync(int transactionId, int optionId, int groupId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var source = RequireGroup(option, groupId);
        var vehicle = RequireVehicleProfile(transaction);

        var line = transaction.DispatchOrder!.Lines.First(l => l.Id == source.DispatchOrderLineId);
        var unitProduct = BuildUnitProduct(line);

        EnsureOrderedQtyNotExceeded(option, source.DispatchOrderLineId, excludeGroupId: null, source.Quantity, line);
        EnsurePayloadNotExceeded(option, vehicle, excludeGroupId: null, unitProduct.Weight * source.Quantity);

        var copy = new OutwardLoadPlanGroup
        {
            OutwardLoadPlanOptionId = optionId,
            DispatchOrderLineId = source.DispatchOrderLineId,
            LoadSequence = option.Groups.Count == 0 ? 1 : option.Groups.Max(g => g.LoadSequence) + 1,
            Color = source.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = supervisorUserId
        };
        ApplyZoneArrangement(copy, source.ZoneLength, source.ZoneWidth, vehicle, unitProduct, source.Quantity);

        _db.OutwardLoadPlanGroups.Add(copy);
        RestackZone(option, source.ZoneLength, source.ZoneWidth, vehicle);

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        copy.DispatchOrderLine = line;
        return ToDto(copy);
    }

    // Splitting never changes total quantity or weight across the option (it's a pure
    // redistribution within the same zone), so unlike Create/Update/Duplicate there's no
    // OrderedQty/payload recheck needed here.
    public async Task<List<LoadPlanGroupDto>> SplitGroupAsync(int transactionId, int optionId, int groupId, string supervisorUserId, int splitQuantity)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var source = RequireGroup(option, groupId);
        RequireNotStarted(source);
        RequireNotLocked(source);
        var vehicle = RequireVehicleProfile(transaction);

        if (splitQuantity <= 0 || splitQuantity >= source.Quantity)
        {
            throw new InvalidOperationException($"Split quantity must be between 1 and {source.Quantity - 1}.");
        }

        var line = transaction.DispatchOrder!.Lines.First(l => l.Id == source.DispatchOrderLineId);
        var unitProduct = BuildUnitProduct(line);

        var newGroup = new OutwardLoadPlanGroup
        {
            OutwardLoadPlanOptionId = optionId,
            DispatchOrderLineId = source.DispatchOrderLineId,
            LoadSequence = option.Groups.Count == 0 ? 1 : option.Groups.Max(g => g.LoadSequence) + 1,
            Color = source.Color,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = supervisorUserId
        };

        ApplyZoneArrangement(source, source.ZoneLength, source.ZoneWidth, vehicle, unitProduct, source.Quantity - splitQuantity);
        ApplyZoneArrangement(newGroup, source.ZoneLength, source.ZoneWidth, vehicle, unitProduct, splitQuantity);
        source.UpdatedAt = DateTime.UtcNow;

        _db.OutwardLoadPlanGroups.Add(newGroup);
        RestackZone(option, source.ZoneLength, source.ZoneWidth, vehicle);

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        newGroup.DispatchOrderLine = line;
        source.DispatchOrderLine ??= line;
        return new List<LoadPlanGroupDto> { ToDto(source), ToDto(newGroup) };
    }

    // Re-stows every zone that currently holds anything, closing any gap left by a delete/move -
    // since placement is pure additive stacking within a fixed zone footprint (no cross-zone
    // relocation, no collision search), compacting is just re-running RestackZone per zone.
    public async Task<List<LoadPlanGroupDto>> CompactGroupsAsync(int transactionId, int optionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var vehicle = RequireVehicleProfile(transaction);

        foreach (var zone in option.Groups.Select(g => (g.ZoneLength, g.ZoneWidth)).Distinct().ToList())
        {
            RestackZone(option, zone.ZoneLength, zone.ZoneWidth, vehicle);
        }

        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);
        return option.Groups.OrderBy(g => g.LoadSequence).Select(ToDto).ToList();
    }

    public async Task<LoadPlanGroupDto> SetGroupLockAsync(int transactionId, int optionId, int groupId, string supervisorUserId, bool locked)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        RequireEditableStatus(transaction);
        var group = RequireGroup(option, groupId);

        group.IsLocked = locked;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        return ToDto(group);
    }

    public async Task<LoadPlanValidationDto> ValidateOptionAsync(int transactionId, int optionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = RequireOption(transaction, optionId);
        var vehicle = RequireVehicleProfile(transaction);

        var validation = LoadPlanValidator.Validate(BuildPlacedItems(option.Groups), vehicle);

        return new LoadPlanValidationDto(
            validation.Warnings.Select(w => new LoadWarningDto(w.RuleCode, w.Message)).ToList(),
            ToDto(validation.Simulation, vehicle));
    }

    // ---------- Actual Loading Confirmation ----------

    public async Task StartConfirmationAsync(int transactionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = transaction.LoadPlanOptions.FirstOrDefault(o => o.IsSelected)
            ?? throw new InvalidOperationException("No load plan option is selected for this job.");

        if (option.Groups.Count == 0)
        {
            throw new InvalidOperationException("The selected option has no placed groups yet.");
        }

        transaction.ActualLoadingStartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<LoadConfirmationStepDto>> GetConfirmationStepsAsync(int transactionId, string supervisorUserId)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        var option = transaction.LoadPlanOptions.FirstOrDefault(o => o.IsSelected)
            ?? throw new InvalidOperationException("No load plan option is selected for this job.");

        return option.Groups.OrderBy(g => g.LoadSequence).Select((g, i) => new LoadConfirmationStepDto(
            i + 1, g.Id, g.DispatchOrderLineId, g.DispatchOrderLine?.ProductName ?? "Item", ZoneCode(g), g.Quantity,
            g.ConfirmationStatus.ToString(), g.ActualQuantity, g.ActualNotes, g.Photos.Select(p => p.Id).ToList())).ToList();
    }

    public async Task<LoadConfirmationStepDto> StartGroupAsync(int transactionId, int groupId, string supervisorUserId) =>
        await TransitionGroupAsync(transactionId, groupId, supervisorUserId, (group, userId) =>
        {
            group.ConfirmationStatus = LoadGroupConfirmationStatus.Started;
            group.ConfirmedByUserId = userId;
        });

    public async Task<LoadConfirmationStepDto> MarkGroupLoadedAsync(int transactionId, int groupId, string supervisorUserId, ConfirmLoadGroupLoadedRequest request) =>
        await TransitionGroupAsync(transactionId, groupId, supervisorUserId, (group, userId) =>
        {
            group.ConfirmationStatus = LoadGroupConfirmationStatus.Loaded;
            group.ActualQuantity = request.ActualQuantity ?? group.Quantity;
            group.ConfirmedAt = DateTime.UtcNow;
            group.ConfirmedByUserId = userId;
        });

    public async Task<LoadConfirmationStepDto> MarkGroupMismatchAsync(int transactionId, int groupId, string supervisorUserId, ConfirmLoadGroupMismatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Notes are required when reporting a mismatch.");
        }

        return await TransitionGroupAsync(transactionId, groupId, supervisorUserId, (group, userId) =>
        {
            group.ConfirmationStatus = LoadGroupConfirmationStatus.Mismatch;
            group.ActualQuantity = request.ActualQuantity ?? group.Quantity;
            group.ActualNotes = request.Notes;
            group.ConfirmedAt = DateTime.UtcNow;
            group.ConfirmedByUserId = userId;
        });
    }

    public async Task<LoadConfirmationStepDto> MarkGroupShortLoadAsync(int transactionId, int groupId, string supervisorUserId, ConfirmLoadGroupShortLoadRequest request) =>
        await TransitionGroupAsync(transactionId, groupId, supervisorUserId, (group, userId) =>
        {
            if (request.ActualQuantity >= group.Quantity)
            {
                throw new InvalidOperationException("Short-load quantity must be less than the planned quantity - use 'loaded' instead.");
            }

            group.ConfirmationStatus = LoadGroupConfirmationStatus.ShortLoad;
            group.ActualQuantity = request.ActualQuantity;
            group.ActualNotes = request.Notes;
            group.ConfirmedAt = DateTime.UtcNow;
            group.ConfirmedByUserId = userId;
        });

    public async Task<LoadConfirmationStepDto> SkipGroupAsync(int transactionId, int groupId, string supervisorUserId, ConfirmLoadGroupSkipRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
        {
            throw new InvalidOperationException("Notes are required when skipping a group.");
        }

        return await TransitionGroupAsync(transactionId, groupId, supervisorUserId, (group, userId) =>
        {
            group.ConfirmationStatus = LoadGroupConfirmationStatus.Skipped;
            group.ActualNotes = request.Notes;
            group.ConfirmedAt = DateTime.UtcNow;
            group.ConfirmedByUserId = userId;
        });
    }

    public async Task<LoadConfirmationStepDto> AddGroupPhotoAsync(int transactionId, int groupId, string supervisorUserId, string fileName, Stream content)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        RequireEditableStatus(transaction);
        FindGroupAcrossOptions(transaction, groupId);

        var filePath = await _photoStorage.SaveAsync($"outward-{transactionId}", fileName, content);
        _db.OutwardPhotoEvidences.Add(new OutwardPhotoEvidence
        {
            OutwardTransactionId = transactionId,
            OutwardLoadPlanGroupId = groupId,
            Type = OutwardPhotoType.LoadGroupConfirmation,
            FilePath = filePath,
            CapturedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        return await BuildStepDtoAsync(transactionId, groupId);
    }

    // Called from OutwardService.CompleteAsync. True (i.e. not a blocker) when
    // the job never used the 3D flow at all, so existing jobs are unaffected.
    public async Task<bool> AreAllSelectedGroupsResolvedAsync(int transactionId)
    {
        var option = await _db.OutwardLoadPlanOptions
            .Include(o => o.Groups)
            .Where(o => o.OutwardTransactionId == transactionId && o.IsSelected)
            .FirstOrDefaultAsync();

        if (option is null || option.Groups.Count == 0)
        {
            return true;
        }

        return option.Groups.All(g => g.ConfirmationStatus is not (LoadGroupConfirmationStatus.NotStarted or LoadGroupConfirmationStatus.Started));
    }

    // Called from OutwardService.RestartLoadingAsync so a supervisor can redo Actual Loading
    // Confirmation on a job that was already marked Completed - every group in the selected
    // option goes back to NotStarted so the Confirm Loading screen starts fresh (0 of N
    // confirmed), same as AreAllSelectedGroupsResolvedAsync's "no groups = nothing to reset" case.
    public async Task ResetConfirmationForRestartAsync(int transactionId)
    {
        var groups = await _db.OutwardLoadPlanGroups
            .Where(g => g.OutwardLoadPlanOption!.OutwardTransactionId == transactionId && g.OutwardLoadPlanOption.IsSelected)
            .ToListAsync();

        foreach (var group in groups)
        {
            group.ConfirmationStatus = LoadGroupConfirmationStatus.NotStarted;
            group.ActualQuantity = null;
            group.ActualNotes = null;
            group.ConfirmedAt = null;
            group.ConfirmedByUserId = null;
        }

        await _db.SaveChangesAsync();
    }

    // ---------- Internals ----------

    private async Task<LoadConfirmationStepDto> TransitionGroupAsync(
        int transactionId, int groupId, string supervisorUserId, Action<OutwardLoadPlanGroup, string> transition)
    {
        var transaction = await GetOwnedTransactionAsync(transactionId, supervisorUserId);
        RequireEditableStatus(transaction);
        var group = FindGroupAcrossOptions(transaction, groupId);

        var option = transaction.LoadPlanOptions.First(o => o.Groups.Any(g => g.Id == groupId));
        if (!option.IsSelected)
        {
            throw new InvalidOperationException("Only the selected option's groups can be confirmed.");
        }

        transition(group, supervisorUserId);
        await _db.SaveChangesAsync();
        await BroadcastLoadPlanChangedAsync(transactionId);

        return await BuildStepDtoAsync(transactionId, groupId);
    }

    private async Task<LoadConfirmationStepDto> BuildStepDtoAsync(int transactionId, int groupId)
    {
        var option = await _db.OutwardLoadPlanOptions
            .Include(o => o.Groups).ThenInclude(g => g.DispatchOrderLine)
            .Include(o => o.Groups).ThenInclude(g => g.Photos)
            .Where(o => o.OutwardTransactionId == transactionId && o.IsSelected)
            .FirstAsync();

        var ordered = option.Groups.OrderBy(g => g.LoadSequence).ToList();
        var index = ordered.FindIndex(g => g.Id == groupId);
        var group = ordered[index];

        return new LoadConfirmationStepDto(
            index + 1, group.Id, group.DispatchOrderLineId, group.DispatchOrderLine?.ProductName ?? "Item", ZoneCode(group), group.Quantity,
            group.ConfirmationStatus.ToString(), group.ActualQuantity, group.ActualNotes, group.Photos.Select(p => p.Id).ToList());
    }

    private static OutwardLoadPlanGroup FindGroupAcrossOptions(OutwardTransaction transaction, int groupId) =>
        transaction.LoadPlanOptions.SelectMany(o => o.Groups).FirstOrDefault(g => g.Id == groupId)
            ?? throw new KeyNotFoundException("Load plan group not found.");

    // A zone's fixed floor rectangle: thirds of vehicle length (Front/Middle/Back) x halves of
    // vehicle width (Left/Right), full vehicle height available. Every group placed in the same
    // zone shares this exact X/Z origin - they only ever differ in Y (see RestackZone).
    private static (double OriginXCm, double OriginZCm, double ZoneWidthCm, double ZoneLengthCm) ZoneFootprint(
        LoadZoneLength zoneLength, LoadZoneWidth zoneWidth, VehicleProfile vehicle)
    {
        var zoneLengthCm = vehicle.Length / 3.0;
        var zoneWidthCm = vehicle.Width / 2.0;
        var originZ = zoneLength switch
        {
            LoadZoneLength.Front => 0.0,
            LoadZoneLength.Middle => zoneLengthCm,
            _ => zoneLengthCm * 2
        };
        var originX = zoneWidth == LoadZoneWidth.Left ? 0.0 : zoneWidthCm;
        return (originX, originZ, zoneWidthCm, zoneLengthCm);
    }

    private readonly record struct ZoneGrid(int Rows, int Columns, int Layers, double DimX, double DimY, double DimZ);

    // Cosmetic auto-arrangement only - always succeeds (adds layers instead of rejecting) since
    // geometric fitment is intentionally not validated. "1 layer preferred" falls out naturally:
    // it only grows past 1 once quantity exceeds what a single layer of the zone's footprint holds.
    private static ZoneGrid ComputeZoneGrid(double zoneWidthCm, double zoneLengthCm, ProductItem unitProduct, int quantity)
    {
        var columns = Math.Max(1, (int)(zoneWidthCm / unitProduct.Width));
        var rows = Math.Max(1, (int)(zoneLengthCm / unitProduct.Length));
        var perLayer = Math.Max(1, columns * rows);
        var layers = Math.Max(1, (int)Math.Ceiling(quantity / (double)perLayer));
        return new ZoneGrid(rows, columns, layers, columns * unitProduct.Width, layers * unitProduct.Height, rows * unitProduct.Length);
    }

    private static ProductItem BuildUnitProduct(DispatchOrderLine line)
    {
        var unitProduct = new ProductItem
        {
            Sku = line.Id.ToString(),
            Description = line.ProductName,
            Quantity = 1,
            Length = (double)(line.Product?.LengthCm ?? 0),
            Width = (double)(line.Product?.WidthCm ?? 0),
            Height = (double)(line.Product?.HeightCm ?? 0),
            Weight = (double)(line.Product?.WeightKg ?? 0),
            // Not used for stacking rejection any more (nothing rejects), kept only because
            // BuildPlacedItems/LoadPlanValidator still read it for the advisory rule panel.
            IsStackable = line.Product?.IsStackable ?? true,
            MaxStackLayers = line.Product?.MaxStackLayers ?? int.MaxValue
        };

        if (unitProduct.Length <= 0 || unitProduct.Width <= 0 || unitProduct.Height <= 0)
        {
            throw new InvalidOperationException("This product has no dimensions on file - link it to a Product master record first.");
        }

        return unitProduct;
    }

    private (LoadZoneLength ZoneLength, LoadZoneWidth ZoneWidth, ProductItem UnitProduct, DispatchOrderLine Line) ResolveZoneRequest(
        OutwardTransaction transaction, PlaceLoadGroupInZoneRequest request)
    {
        var line = transaction.DispatchOrder!.Lines.FirstOrDefault(l => l.Id == request.DispatchOrderLineId)
            ?? throw new InvalidOperationException("This dispatch order line is invalid for this job.");

        if (request.Quantity <= 0)
        {
            throw new InvalidOperationException("Quantity must be greater than zero.");
        }

        var unitProduct = BuildUnitProduct(line);
        var zoneLength = ParseEnum<LoadZoneLength>("zone", request.ZoneLength);
        var zoneWidth = ParseEnum<LoadZoneWidth>("zone", request.ZoneWidth);

        return (zoneLength, zoneWidth, unitProduct, line);
    }

    // Hard rule: total quantity placed for a SKU across every zone in this option can never
    // exceed what Office actually picked for it (DispatchOrderLine.OrderedQty) - checked against
    // every other group for the same line plus the quantity this call is about to add/replace.
    // This is the one hard placement rule this redesign introduces; nothing checked this before.
    private static void EnsureOrderedQtyNotExceeded(
        OutwardLoadPlanOption option, int dispatchOrderLineId, int? excludeGroupId, int requestedQty, DispatchOrderLine line)
    {
        var alreadyPlaced = option.Groups
            .Where(g => g.DispatchOrderLineId == dispatchOrderLineId && g.Id != excludeGroupId)
            .Sum(g => g.Quantity);

        var total = alreadyPlaced + requestedQty;
        if (total > line.OrderedQty)
        {
            throw new InvalidOperationException(
                $"Cannot place {requestedQty} of {line.ProductName}: {alreadyPlaced} already placed in other zones, " +
                $"exceeding the pick-list quantity of {line.OrderedQty:0.#}.");
        }
    }

    // Hard rule: total loaded weight can never exceed the vehicle's payload -
    // checked against every other group already in this option plus the
    // weight this call is about to add (or, for an edit, replace).
    private static void EnsurePayloadNotExceeded(
        OutwardLoadPlanOption option, VehicleProfile vehicle, int? excludeGroupId, double addedWeightKg)
    {
        var existingWeight = option.Groups
            .Where(g => g.Id != excludeGroupId)
            .Sum(g => (double)(g.DispatchOrderLine?.Product?.WeightKg ?? 0) * g.Quantity);

        var totalWeight = existingWeight + addedWeightKg;
        if (totalWeight > vehicle.MaxPayload)
        {
            throw new InvalidOperationException(
                $"Cannot place: total loaded weight would be {totalWeight:0.#} kg, exceeding the vehicle's {vehicle.MaxPayload:0.#} kg payload.");
        }
    }

    // Sets everything about a group's placement except its Y position (RestackZone owns Y,
    // since Y depends on every OTHER group sharing the same zone, not just this one).
    private static void ApplyZoneArrangement(
        OutwardLoadPlanGroup group, LoadZoneLength zoneLength, LoadZoneWidth zoneWidth,
        VehicleProfile vehicle, ProductItem unitProduct, int quantity)
    {
        var (originX, originZ, zoneWidthCm, zoneLengthCm) = ZoneFootprint(zoneLength, zoneWidth, vehicle);
        var grid = ComputeZoneGrid(zoneWidthCm, zoneLengthCm, unitProduct, quantity);

        group.Quantity = quantity;
        group.ZoneLength = zoneLength;
        group.ZoneWidth = zoneWidth;
        group.PositionXCm = (decimal)originX;
        group.PositionZCm = (decimal)originZ;
        group.DimXCm = (decimal)grid.DimX;
        group.DimYCm = (decimal)grid.DimY;
        group.DimZCm = (decimal)grid.DimZ;
        // Orientation is no longer a supervisor decision - fixed default for the cosmetic visual.
        group.Orientation = LoadOrientation.LWH;
        group.Rows = grid.Rows;
        group.Columns = grid.Columns;
        group.Layers = grid.Layers;
    }

    // Re-derives every group's Y position within one zone footprint, in placement order (Load-
    // Sequence), so removing/moving/resizing a group never leaves a gap or an overlap for what's
    // left behind - the closest equivalent of "gravity" this bookkeeping-only model needs.
    private static void RestackZone(OutwardLoadPlanOption option, LoadZoneLength zoneLength, LoadZoneWidth zoneWidth, VehicleProfile vehicle)
    {
        double y = 0;
        // Distinct(): EF Core's automatic relationship fixup can append a newly-Added entity into
        // an already-loaded collection navigation on its own - guard against double-counting the
        // same group reference if that ever runs alongside an explicit add.
        foreach (var g in option.Groups
            .Distinct()
            .Where(g => g.ZoneLength == zoneLength && g.ZoneWidth == zoneWidth)
            .OrderBy(g => g.LoadSequence))
        {
            g.PositionYCm = (decimal)y;
            g.ZoneHeight = DeriveZoneHeight(y, (double)g.DimYCm, vehicle);
            y += (double)g.DimYCm;
        }
    }

    // Human-readable height label only (Front/Left/Back/Right are supervisor input now, not
    // derived) - same thirds convention as before (Bottom = near-floor third, ... = top third).
    private static LoadZoneHeight DeriveZoneHeight(double positionY, double dimY, VehicleProfile vehicle)
    {
        var centerY = positionY + dimY / 2;
        return (LoadZoneHeight)ZoneThirdIndex(centerY, vehicle.Height);
    }

    private static int ZoneThirdIndex(double center, double total) =>
        total <= 0 ? 1 : Math.Clamp((int)(center / (total / 3.0)), 0, 2);

    private static void RequireNotLocked(OutwardLoadPlanGroup group)
    {
        if (group.IsLocked)
        {
            throw new InvalidOperationException("This group is locked - unlock it first.");
        }
    }

    // Expands persisted groups (which store only Rows/Columns/Layers + the
    // group's overall bounding box) back into individual per-carton
    // PlacedItems for the validator - deriving each unit's dims arithmetically
    // from the stored bounding box divided by the grid counts, rather than
    // storing per-carton geometry a second time.
    private static List<PlacedItem> BuildPlacedItems(IEnumerable<OutwardLoadPlanGroup> groups)
    {
        var items = new List<PlacedItem>();
        foreach (var group in groups)
        {
            if (group.Rows <= 0 || group.Columns <= 0 || group.Layers <= 0 || group.Quantity <= 0)
            {
                continue;
            }

            var unitDimX = (double)group.DimXCm / group.Columns;
            var unitDimY = (double)group.DimYCm / group.Layers;
            var unitDimZ = (double)group.DimZCm / group.Rows;
            var unitWeight = group.DispatchOrderLine?.Product?.WeightKg is decimal w ? (double)w : 0;
            var description = group.DispatchOrderLine?.ProductName ?? "Item";
            var engineOrientation = ToEngineOrientation(group.Orientation);
            var isStackable = group.DispatchOrderLine?.Product?.IsStackable ?? true;
            var maxStackLayers = group.DispatchOrderLine?.Product?.MaxStackLayers ?? int.MaxValue;

            var placed = 0;
            for (var l = 0; l < group.Layers && placed < group.Quantity; l++)
            {
                for (var r = 0; r < group.Rows && placed < group.Quantity; r++)
                {
                    for (var c = 0; c < group.Columns && placed < group.Quantity; c++)
                    {
                        items.Add(new PlacedItem
                        {
                            Product = new ProductItem
                            {
                                Sku = group.DispatchOrderLineId.ToString(),
                                Description = description,
                                Quantity = 1,
                                Length = unitDimX,
                                Width = unitDimY,
                                Height = unitDimZ,
                                Weight = unitWeight,
                                IsStackable = isStackable,
                                MaxStackLayers = maxStackLayers
                            },
                            Placement = new Placement
                            {
                                Position = new Vector3D(
                                    (double)group.PositionXCm + c * unitDimX,
                                    (double)group.PositionYCm + l * unitDimY,
                                    (double)group.PositionZCm + r * unitDimZ),
                                Orientation = engineOrientation,
                                DimX = unitDimX,
                                DimY = unitDimY,
                                DimZ = unitDimZ
                            },
                            StackLevel = l,
                            SupportArea = 1,
                            LoadSequence = group.LoadSequence,
                            Color = group.Color
                        });
                        placed++;
                    }
                }
            }
        }

        return items;
    }

    private static string ColorForLine(int dispatchOrderLineId, OutwardTransaction transaction)
    {
        var distinctLineIds = transaction.DispatchOrder!.Lines.Select(l => l.Id).Distinct().ToList();
        var index = distinctLineIds.IndexOf(dispatchOrderLineId);
        return Palette[index < 0 ? 0 : index % Palette.Length];
    }

    private static string ZoneCode(OutwardLoadPlanGroup g) =>
        $"{g.ZoneLength.ToString()[0]}-{g.ZoneWidth.ToString()[0]}-{g.ZoneHeight.ToString()[0]}";

    private static VehicleProfile RequireVehicleProfile(OutwardTransaction transaction)
    {
        var vehicle = transaction.Vehicle;
        if (vehicle?.MaxWeightKg is null || vehicle.LengthCm is null || vehicle.WidthCm is null || vehicle.HeightCm is null)
        {
            throw new InvalidOperationException("Vehicle capacity is not on file for this job yet.");
        }

        return new VehicleProfile
        {
            Name = vehicle.Number,
            Length = (double)vehicle.LengthCm.Value,
            Width = (double)vehicle.WidthCm.Value,
            Height = (double)vehicle.HeightCm.Value,
            MaxPayload = (double)vehicle.MaxWeightKg.Value
        };
    }

    private static void RequireEditableStatus(OutwardTransaction transaction)
    {
        if (transaction.Status is not (OutwardStatus.Docked or OutwardStatus.Loading))
        {
            throw new InvalidOperationException("The load plan can only be edited after dock-in and before completion.");
        }
    }

    private static void RequireNotStarted(OutwardLoadPlanGroup group)
    {
        if (group.ConfirmationStatus != LoadGroupConfirmationStatus.NotStarted)
        {
            throw new InvalidOperationException("This group can't be edited once loading confirmation has started for it.");
        }
    }

    private static OutwardLoadPlanOption RequireOption(OutwardTransaction transaction, int optionId) =>
        transaction.LoadPlanOptions.FirstOrDefault(o => o.Id == optionId)
            ?? throw new KeyNotFoundException("Load plan option not found.");

    private static OutwardLoadPlanGroup RequireGroup(OutwardLoadPlanOption option, int groupId) =>
        option.Groups.FirstOrDefault(g => g.Id == groupId)
            ?? throw new KeyNotFoundException("Load plan group not found.");

    private async Task<OutwardTransaction> GetOwnedTransactionAsync(int transactionId, string supervisorUserId)
    {
        // Split query: the four collection includes otherwise multiply into one giant
        // cartesian join whose row count explodes with every placed group - the difference
        // between milliseconds and multi-second stalls once a load has many groups.
        var transaction = await _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.DispatchOrder!).ThenInclude(d => d.Lines).ThenInclude(l => l.Product)
            .Include(t => t.LoadPlanOptions).ThenInclude(o => o.Groups).ThenInclude(g => g.DispatchOrderLine!).ThenInclude(l => l.Product)
            .Include(t => t.LoadPlanOptions).ThenInclude(o => o.Groups).ThenInclude(g => g.Photos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == transactionId)
            ?? throw new KeyNotFoundException("Job not found.");

        if (transaction.AssignedSupervisorUserId != supervisorUserId)
        {
            throw new UnauthorizedAccessException("This job is not assigned to you.");
        }

        return transaction;
    }

    private static LoadPlanGroupDto ToDto(OutwardLoadPlanGroup g) => new(
        g.Id, g.DispatchOrderLineId, g.DispatchOrderLine?.ProductName ?? "Item", g.Quantity,
        g.ZoneLength.ToString(), g.ZoneWidth.ToString(), g.ZoneHeight.ToString(), ZoneCode(g),
        (double)g.PositionXCm, (double)g.PositionYCm, (double)g.PositionZCm,
        (double)g.DimXCm, (double)g.DimYCm, (double)g.DimZCm,
        g.Orientation.ToString(), g.Rows, g.Columns, g.Layers, g.LoadSequence, g.Color, g.IsLocked,
        g.ConfirmationStatus.ToString(), g.ActualQuantity, g.ActualNotes, g.ConfirmedAt,
        g.Photos.Select(p => p.Id).ToList());

    private static LoadSimulationDto ToDto(WarehouseGate.LoadPlanning.Simulation.LoadSimulationResult s, VehicleProfile vehicle) => new(
        s.VehicleUtilizationPct, s.WeightUtilizationPct,
        s.CenterOfGravity.X, s.CenterOfGravity.Y, s.CenterOfGravity.Z,
        s.RemainingCapacityKg, s.RemainingVolumeM3, s.RuleViolationCount, s.UnplacedCount, s.OptimizationScore,
        LoadPlanningResultMapper.ComputeBalanceStatus(s.CenterOfGravity, vehicle));

    private static TEnum ParseEnum<TEnum>(string fieldName, string value) where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            throw new InvalidOperationException($"Invalid {fieldName} '{value}'.");
        }

        return result;
    }

    private static Orientation ToEngineOrientation(LoadOrientation orientation) => orientation switch
    {
        LoadOrientation.LWH => Orientation.LWH,
        LoadOrientation.WLH => Orientation.WLH,
        LoadOrientation.LHW => Orientation.LHW,
        LoadOrientation.HLW => Orientation.HLW,
        LoadOrientation.WHL => Orientation.WHL,
        LoadOrientation.HWL => Orientation.HWL,
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };
}

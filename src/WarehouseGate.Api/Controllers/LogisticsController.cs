using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Hubs;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/logistics")]
[Authorize(Roles = "LogisticsManager")]
public class LogisticsController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly AuditService _audit;
    private readonly IHubContext<InwardHub> _hub;

    public LogisticsController(WarehouseGateDbContext db, AuditService audit, IHubContext<InwardHub> hub)
    {
        _db = db;
        _audit = audit;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string CurrentUserName => User.FindFirstValue("displayName") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

    private async Task<int?> GetCallerRegionIdAsync() =>
        await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.RegionId).FirstOrDefaultAsync();

    // A dispatch's From/To warehouse can each legitimately sit in a different region than the
    // uploader's own (e.g. West region warehouse shipping to a North region CFA) - so the pick
    // lists offer the full master, and row-level access is enforced separately by requiring at
    // least one side (From or To) to fall in the caller's own region.
    private async Task<bool> IsRowInCallerScopeAsync(int fromWarehouseId, int toWarehouseId)
    {
        var regionId = await GetCallerRegionIdAsync();
        if (regionId is null)
        {
            return false;
        }

        return await _db.Warehouses.AnyAsync(w =>
            (w.Id == fromWarehouseId || w.Id == toWarehouseId) && w.RegionId == regionId);
    }

    // ============================ WAREHOUSES (full master - From/To can cross regions) ============================

    [HttpGet("warehouses")]
    public async Task<ActionResult<List<WarehouseDto>>> GetWarehouses()
    {
        var full = await _db.Warehouses
            .Include(w => w.Region).Include(w => w.State).Include(w => w.City).Include(w => w.Country)
            .OrderBy(w => w.Name)
            .ToListAsync();

        return Ok(full.Select(w => new WarehouseDto(
            w.Id, w.Name, w.WarehouseType.ToString(),
            w.RegionId, w.Region!.Name, w.StateId, w.State!.Name, w.CityId, w.City!.Name, w.CountryId, w.Country!.Name,
            w.SlaTargetMinutes, w.DockOperatingHoursPerDay, w.ShiftHoursPerDay)).ToList());
    }

    // ============================ VEHICLE LOGISTICS RECORDS ============================

    [HttpGet("vehicle-records")]
    public async Task<ActionResult<List<VehicleLogisticsRecordDto>>> GetVehicleRecords()
    {
        var regionId = await GetCallerRegionIdAsync();
        if (regionId is null)
        {
            return Ok(new List<VehicleLogisticsRecordDto>());
        }

        // No filter parameters on this endpoint at all - Logistics reviews the full upload as one
        // list, so unlike Inward/Outward's history caps there's no "filtered vs unfiltered" case
        // to distinguish. Capped to the most recent 500 records so a region with years of vehicle
        // logistics data can't silently balloon this into an unbounded payload.
        var records = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .Where(r => r.FromWarehouse!.RegionId == regionId || r.ToWarehouse!.RegionId == regionId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(MaxVehicleRecordResults)
            .ToListAsync();

        return Ok(records.Select(ToDto).ToList());
    }

    private const int MaxVehicleRecordResults = 500;

    [HttpPost("vehicle-records")]
    public async Task<ActionResult<VehicleLogisticsRecordDto>> CreateVehicleRecord(UpsertVehicleLogisticsRecordRequest request)
    {
        var validation = await ValidateWarehousesAsync(request.FromWarehouseId, request.ToWarehouseId);
        if (validation is not null)
        {
            return validation;
        }

        var record = new VehicleLogisticsRecord
        {
            VehicleNumber = request.VehicleNumber.Trim(),
            PoNumber = request.PoNumber,
            InwardTransactionId = request.InwardTransactionId,
            TransporterName = request.TransporterName,
            DriverName = request.DriverName,
            DriverPhone = request.DriverPhone,
            VehicleType = request.VehicleType,
            Sku = request.Sku.Trim(),
            SkuCode = request.SkuCode,
            BoxQuantity = request.BoxQuantity,
            DepartureDate = request.DepartureDate,
            EtaDateTime = request.EtaDateTime,
            FromWarehouseId = request.FromWarehouseId,
            ToWarehouseId = request.ToWarehouseId,
            // Always InTransit on creation regardless of what the request sends - manual add and
            // Excel import both start a record's life the same way.
            Status = VehicleLogisticsStatus.InTransit,
            CreatedByUserId = CurrentUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.VehicleLogisticsRecords.Add(record);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", record.Id, AuditAction.Created,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' created.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();

        return await GetVehicleRecordDtoAsync(record.Id);
    }

    [HttpPut("vehicle-records/{id:int}")]
    public async Task<ActionResult<VehicleLogisticsRecordDto>> UpdateVehicleRecord(int id, UpsertVehicleLogisticsRecordRequest request)
    {
        var record = await _db.VehicleLogisticsRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (!await IsRowInCallerScopeAsync(record.FromWarehouseId, record.ToWarehouseId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This record is outside your region." });
        }

        var validation = await ValidateWarehousesAsync(request.FromWarehouseId, request.ToWarehouseId);
        if (validation is not null)
        {
            return validation;
        }

        record.VehicleNumber = request.VehicleNumber.Trim();
        record.PoNumber = request.PoNumber;
        record.InwardTransactionId = request.InwardTransactionId;
        record.TransporterName = request.TransporterName;
        record.DriverName = request.DriverName;
        record.DriverPhone = request.DriverPhone;
        record.VehicleType = request.VehicleType;
        record.Sku = request.Sku.Trim();
        record.SkuCode = request.SkuCode;
        record.BoxQuantity = request.BoxQuantity;
        record.DepartureDate = request.DepartureDate;
        record.EtaDateTime = request.EtaDateTime;
        record.FromWarehouseId = request.FromWarehouseId;
        record.ToWarehouseId = request.ToWarehouseId;
        // Editing lets a Logistics Manager manually override status (e.g. Inactive); leaving it
        // unset in the request keeps whatever the automatic Inward/Outward sync last set.
        if (request.Status is { } status)
        {
            record.Status = status;
        }
        record.UpdatedByUserId = CurrentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", record.Id, AuditAction.Updated,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' updated.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();

        return await GetVehicleRecordDtoAsync(record.Id);
    }

    [HttpDelete("vehicle-records/{id:int}")]
    public async Task<IActionResult> DeleteVehicleRecord(int id)
    {
        var record = await _db.VehicleLogisticsRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (!await IsRowInCallerScopeAsync(record.FromWarehouseId, record.ToWarehouseId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This record is outside your region." });
        }

        // Soft delete - the record stays queryable/filterable by status (matches the "Deleted"
        // status option in the UI) rather than disappearing from the audit trail entirely.
        record.Status = VehicleLogisticsStatus.Deleted;
        record.UpdatedByUserId = CurrentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("VehicleLogisticsRecord", id, AuditAction.Deleted,
            $"Vehicle logistics record for '{record.VehicleNumber}' / SKU '{record.Sku}' deleted.", CurrentUserId, CurrentUserName);
        await BroadcastChangedAsync();
        return NoContent();
    }

    [HttpPost("vehicle-records/upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<VehicleLogisticsUploadResultDto>> UploadVehicleRecords(IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        var allWarehouses = await _db.Warehouses.ToListAsync();
        var regionId = await GetCallerRegionIdAsync();

        List<Domain.VehicleLogisticsRecord> created;
        List<VehicleLogisticsUploadRowErrorDto> errors;
        await using (var stream = file.OpenReadStream())
        {
            (created, errors) = VehicleLogisticsExcelParser.Parse(stream, CurrentUserId, allWarehouses, regionId);
        }

        if (created.Count > 0)
        {
            _db.VehicleLogisticsRecords.AddRange(created);
            await _db.SaveChangesAsync();
            await _audit.LogAsync("VehicleLogisticsRecord", 0, AuditAction.Created,
                $"Imported {created.Count} vehicle logistics record(s) from '{file.FileName}' ({errors.Count} row(s) skipped).",
                CurrentUserId, CurrentUserName);
            await BroadcastChangedAsync();
        }

        return Ok(new VehicleLogisticsUploadResultDto(created.Count, errors));
    }

    // Both warehouses must exist, be distinct, and at least one must fall in the caller's own
    // region - shared by Create and Update so the rule can't drift between the two.
    private async Task<ActionResult?> ValidateWarehousesAsync(int fromWarehouseId, int toWarehouseId)
    {
        if (fromWarehouseId == toWarehouseId)
        {
            return BadRequest(new { message = "From and To warehouse cannot be the same." });
        }

        var warehouseIds = await _db.Warehouses
            .Where(w => w.Id == fromWarehouseId || w.Id == toWarehouseId)
            .Select(w => w.Id)
            .ToListAsync();
        if (!warehouseIds.Contains(fromWarehouseId) || !warehouseIds.Contains(toWarehouseId))
        {
            return BadRequest(new { message = "From/To warehouse not found." });
        }

        if (!await IsRowInCallerScopeAsync(fromWarehouseId, toWarehouseId))
        {
            return BadRequest(new { message = "Neither the From nor the To warehouse is in your region." });
        }

        return null;
    }

    // Additive nudge, not load-bearing - other Logistics Managers viewing the same Dispatch Plan
    // list, and Office users viewing their pending inward/outward Dispatch Plan panels, just
    // refetch on receipt, same pattern as OfficeRealtimeClient's job-change events.
    private Task BroadcastChangedAsync() =>
        _hub.Clients.Groups(InwardHub.LogisticsGroup, InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");

    private async Task<ActionResult<VehicleLogisticsRecordDto>> GetVehicleRecordDtoAsync(int id)
    {
        var record = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .FirstAsync(r => r.Id == id);
        return Ok(ToDto(record));
    }

    private static VehicleLogisticsRecordDto ToDto(Domain.VehicleLogisticsRecord r) => new(
        r.Id, r.VehicleNumber, r.PoNumber, r.InwardTransactionId, r.TransporterName, r.DriverName, r.DriverPhone,
        r.VehicleType, r.Sku, r.SkuCode, r.BoxQuantity,
        r.DepartureDate, r.EtaDateTime,
        r.FromWarehouseId, r.FromWarehouse!.Name, r.ToWarehouseId, r.ToWarehouse!.Name,
        r.Status.ToString(), r.CreatedAtUtc);
}

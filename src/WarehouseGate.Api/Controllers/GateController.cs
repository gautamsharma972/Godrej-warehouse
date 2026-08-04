using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/gate")]
[Authorize(Roles = "Security")]
public class GateController : ControllerBase
{
    private readonly InwardService _inwardService;
    private readonly WarehouseGateDbContext _db;

    public GateController(InwardService inwardService, WarehouseGateDbContext db)
    {
        _inwardService = inwardService;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<int?> GetCallerWarehouseIdAsync() =>
        await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.WarehouseId).FirstOrDefaultAsync();

    [HttpPost("checkin")]
    public async Task<ActionResult<InwardJobDto>> CheckIn(GateCheckInRequest request)
    {
        try
        {
            var job = await _inwardService.CheckInAsync(request, CurrentUserId);
            return Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("vehicle-master/{vehicleNumber}")]
    public async Task<ActionResult<VehicleMasterDto>> GetVehicleMaster(string vehicleNumber)
    {
        var master = await _inwardService.GetVehicleMasterAsync(vehicleNumber);
        return master is null ? NotFound() : Ok(master);
    }

    // Backs the mobile app's Outward gate-in vehicle picker - the real Admin Vehicle Registry
    // (api/admin/vehicles is SuperAdmin-only), exposed read-only to Security so it can search/select
    // a known plate; free-text entry still works for a vehicle not in the registry yet
    // (CreateGateArrivalAsync creates one on the fly).
    [HttpGet("vehicle-registry")]
    public async Task<ActionResult<List<VehicleDto>>> GetVehicleRegistry() =>
        Ok(await _db.Vehicles.OrderBy(v => v.Number)
            .Select(v => new VehicleDto(v.Id, v.Number, v.MaxWeightKg, v.LengthCm, v.WidthCm, v.HeightCm))
            .ToListAsync());

    // Backs the Supervisor's Dock-In vehicle picker on OutwardJobDetailPage - Outward jobs Office
    // has already generated a pick list for (so vehicle number/driver/transporter are already known
    // from the Dispatch Plan or a completed Link Vehicle, see OutwardService.GeneratePickListFromDispatchPlanAsync/
    // LinkVehicleAsync) but that haven't been gated in at THIS warehouse yet. Unrelated to Security's
    // own gate-in picker above (see GetVehicleRegistry) - left as-is, this app iteration doesn't
    // change Supervisor-facing Outward behavior.
    [HttpGet("vehicle-masters")]
    public async Task<ActionResult<List<VehicleMasterDto>>> GetVehicleMasters()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<VehicleMasterDto>());
        }

        var pending = await _db.OutwardTransactions
            .Include(t => t.Vehicle)
            .Include(t => t.DispatchOrder)
            .Where(t => t.WarehouseId == warehouseId && t.GateInTime == null
                && t.Status != OutwardStatus.Completed && t.Vehicle != null)
            .OrderBy(t => t.CreatedTime)
            .ToListAsync();

        return Ok(pending
            .Where(t => !string.IsNullOrWhiteSpace(t.Vehicle!.Number))
            .Select(t => new VehicleMasterDto(t.Vehicle!.Number, t.DriverName, t.DriverMobile, t.TransporterName, t.DispatchOrder!.DispatchOrderNumber))
            .ToList());
    }

    // Pre-registered inward shipments (uploaded by Logistics Manager) for the caller's own
    // warehouse - backs the mobile app's vehicle-number picker on Gate Check-In.
    [HttpGet("expected-shipments")]
    public async Task<ActionResult<List<VehicleLogisticsRecordDto>>> GetExpectedShipments()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<VehicleLogisticsRecordDto>());
        }

        // The security user's own warehouse is the receiving ("To") side of an inward shipment.
        // Still shown after the source warehouse's Outward job has claimed and dispatched it
        // (Status flips InTransit -> InProgress at that point, see VehicleLogisticsStatus) - the
        // shipment is physically in transit toward here, exactly when Security needs to be able
        // to pick it for gate check-in. Only drops off once actually checked in here.
        // VehicleNumber is usually null now (no longer required in the Dispatch Plan Excel, and
        // Office no longer pre-tags a vehicle before arrival - see InwardService.LinkVehicleAsync,
        // which links AFTER Security's Gate Check-in instead). Rows with no vehicle number yet
        // aren't pickable here at all, only legacy rows that already had one set directly
        // (Excel/manual entry, still possible, just no longer required).
        var records = await _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .Where(r => r.ToWarehouseId == warehouseId
                && r.VehicleNumber != null
                && r.ConsumedByInwardTransactionId == null
                && (r.Status == VehicleLogisticsStatus.InTransit
                    || (r.Status == VehicleLogisticsStatus.InProgress && r.ConsumedByOutwardTransactionId != null)))
            .OrderBy(r => r.EtaDateTime)
            .ToListAsync();

        return Ok(records.Select(r => new VehicleLogisticsRecordDto(
            r.Id, r.VehicleNumber, r.PoNumber, r.InwardTransactionId, r.TransporterName, r.DriverName, r.DriverPhone,
            r.VehicleType, r.Sku, r.SkuCode, r.BoxQuantity,
            // Not surfaced on this picker (Security just needs to pick a vehicle to check in) -
            // see LogisticsController.ResolveLiveDispatchDataAsync for where these are actually resolved.
            null, null, null,
            r.DepartureDate, r.EtaDateTime,
            r.FromWarehouseId, r.FromWarehouse!.Name, r.ToWarehouseId, r.ToWarehouse!.Name,
            r.Status.ToString(), r.CreatedAtUtc)).ToList());
    }

    // Real Transporter master data for Security's Transporter picker - api/admin/transporters is
    // SuperAdmin-only, so this exposes a read-only, active-only subset to the Security role instead
    // of the mobile app falling back to a hardcoded list.
    [HttpGet("transporters")]
    public async Task<ActionResult<List<string>>> GetTransporters()
    {
        var names = await _db.Transporters
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync();
        return Ok(names);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<List<InwardJobDto>>> GetTransactions(
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? vehicleNumber = null,
        [FromQuery] string? poNumber = null,
        [FromQuery] DateTime? date = null)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        return Ok(await _inwardService.GetForSecurityAsync(warehouseId, activeOnly, vehicleNumber, poNumber, date));
    }

    [HttpPost("{id:int}/photos")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<InwardJobDto>> AddPhoto(int id, [FromForm] PhotoType type, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _inwardService.AddGatePhotoAsync(id, CurrentUserId, type, file.FileName, stream));
    }

    [HttpPost("{id:int}/documents")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<InwardJobDto>> AddDocument(int id, [FromForm] DocumentType type, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _inwardService.AddGateDocumentAsync(id, CurrentUserId, type, file.FileName, stream));
    }

    [HttpGet("transactions/pending-exit")]
    public async Task<ActionResult<List<InwardJobDto>>> GetPendingExit([FromQuery] string? vehicleNumber)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        return Ok(await _inwardService.GetPendingExitAsync(warehouseId, vehicleNumber));
    }

    [HttpPost("{id:int}/exit")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<InwardJobDto>> RecordExit(int id, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        var warehouseId = await GetCallerWarehouseIdAsync();
        await using var stream = file.OpenReadStream();
        return await Handle(() => _inwardService.RecordExitAsync(id, CurrentUserId, warehouseId, file.FileName, stream));
    }

    private async Task<ActionResult<InwardJobDto>> Handle(Func<Task<InwardJobDto>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

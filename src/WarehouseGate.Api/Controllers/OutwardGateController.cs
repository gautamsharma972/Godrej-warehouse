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
[Route("api/outward-gate")]
[Authorize(Roles = "Security")]
public class OutwardGateController : ControllerBase
{
    private readonly OutwardService _outwardService;
    private readonly WarehouseGateDbContext _db;

    public OutwardGateController(OutwardService outwardService, WarehouseGateDbContext db)
    {
        _outwardService = outwardService;
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<int?> GetCallerWarehouseIdAsync() =>
        await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.WarehouseId).FirstOrDefaultAsync();

    [HttpPost("checkin")]
    public async Task<ActionResult<OutwardGateArrivalDto>> CheckIn(OutwardGateArrivalCheckInRequest request)
    {
        try
        {
            var arrival = await _outwardService.CreateGateArrivalAsync(request, CurrentUserId);
            return Ok(arrival);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetTransactions(
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? vehicleNumber = null,
        [FromQuery] string? dispatchOrderNumber = null,
        [FromQuery] DateTime? date = null)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        return Ok(await _outwardService.GetForSecurityAsync(warehouseId, activeOnly, vehicleNumber, dispatchOrderNumber, date));
    }

    [HttpPost("{id:int}/photos")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<OutwardGateArrivalDto>> AddPhoto(int id, [FromForm] OutwardPhotoType type, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _outwardService.AddGateArrivalPhotoAsync(id, CurrentUserId, type, file.FileName, stream));
    }

    [HttpGet("transactions/pending-exit")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetPendingExit([FromQuery] string? vehicleNumber)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        return Ok(await _outwardService.GetPendingExitAsync(warehouseId, vehicleNumber));
    }

    [HttpPost("{id:int}/exit")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<OutwardJobDto>> RecordExit(int id, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        var warehouseId = await GetCallerWarehouseIdAsync();
        await using var stream = file.OpenReadStream();
        return await Handle(() => _outwardService.RecordExitAsync(id, CurrentUserId, warehouseId, file.FileName, stream));
    }

    private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
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

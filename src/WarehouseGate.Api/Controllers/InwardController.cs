using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/inward")]
[Authorize]
public class InwardController : ControllerBase
{
    private readonly InwardService _inwardService;

    public InwardController(InwardService inwardService)
    {
        _inwardService = inwardService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("available")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<InwardJobDto>>> GetAvailable() =>
        Ok(await _inwardService.GetAvailableAsync());

    [HttpGet("mine")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<InwardJobDto>>> GetMine() =>
        Ok(await _inwardService.GetMineAsync(CurrentUserId));

    [HttpGet("history")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<InwardJobDto>>> GetHistory(
        [FromQuery] string? vehicleNumber, [FromQuery] string? poNumber, [FromQuery] DateTime? date) =>
        Ok(await _inwardService.GetHistoryForSupervisorAsync(CurrentUserId, vehicleNumber, poNumber, date));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InwardJobDto>> GetById(int id)
    {
        var job = await _inwardService.GetByIdAsync(id);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("{id:int}/outward-reference")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardOutwardReferenceDto>> GetOutwardReference(int id)
    {
        try
        {
            return Ok(await _inwardService.GetOutwardReferenceAsync(id, CurrentUserId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/claim")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardJobDto>> Claim(int id) =>
        await Handle(() => _inwardService.ClaimAsync(id, CurrentUserId));

    [HttpPost("{id:int}/dock-in")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardJobDto>> DockIn(int id, DockInRequest request) =>
        await Handle(() => _inwardService.DockInAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardJobDto>> StartUnloading(int id) =>
        await Handle(() => _inwardService.StartUnloadingAsync(id, CurrentUserId));

    [HttpPost("{id:int}/photos")]
    [Authorize(Roles = "Supervisor")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<InwardJobDto>> AddPhoto(int id, [FromForm] PhotoType type, IFormFile file, [FromForm] int? purchaseOrderLineId = null)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _inwardService.AddPhotoAsync(id, CurrentUserId, type, file.FileName, stream, purchaseOrderLineId));
    }

    // Office also needs this to power the SKU picker when correcting Mismatch SKU Details rows
    // during pre-GRN verification (see OfficeController.UpdateInspection).
    [HttpGet("sku-master")]
    [Authorize(Roles = "Supervisor,Office")]
    public async Task<ActionResult<List<SkuMasterItemDto>>> SearchSkuMaster([FromQuery] string? search) =>
        Ok(await _inwardService.SearchSkuMasterAsync(search));

    [HttpPost("{id:int}/inspection")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardJobDto>> SubmitInspection(int id, SubmitInspectionRequest request) =>
        await Handle(() => _inwardService.SubmitInspectionAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<InwardJobDto>> Complete(int id) =>
        await Handle(() => _inwardService.CompleteAsync(id, CurrentUserId));

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

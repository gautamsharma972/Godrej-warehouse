using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/outward")]
[Authorize]
public class OutwardController : ControllerBase
{
    private readonly OutwardService _outwardService;
    private readonly OutwardLoadPlanService _loadPlanService;

    public OutwardController(OutwardService outwardService, OutwardLoadPlanService loadPlanService)
    {
        _outwardService = outwardService;
        _loadPlanService = loadPlanService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet("available")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetAvailable() =>
        Ok(await _outwardService.GetAvailableAsync());

    [HttpGet("mine")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetMine() =>
        Ok(await _outwardService.GetMineAsync(CurrentUserId));

    [HttpGet("history")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetHistory(
        [FromQuery] string? vehicleNumber, [FromQuery] string? dispatchOrderNumber, [FromQuery] DateTime? date) =>
        Ok(await _outwardService.GetHistoryForSupervisorAsync(CurrentUserId, vehicleNumber, dispatchOrderNumber, date));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OutwardJobDto>> GetById(int id)
    {
        var job = await _outwardService.GetByIdAsync(id);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("{id:int}/load-plan")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanResultDto>> GetLoadPlan(int id)
    {
        try
        {
            var plan = await _outwardService.GetLoadPlanAsync(id, CurrentUserId);
            return plan is null
                ? BadRequest(new { message = "Vehicle capacity is not on file for this job yet." })
                : Ok(plan);
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
    public async Task<ActionResult<OutwardJobDto>> Claim(int id) =>
        await Handle(() => _outwardService.ClaimAsync(id, CurrentUserId));

    [HttpPost("{id:int}/dock-in")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> DockIn(int id, DockInOutwardRequest request) =>
        await Handle(() => _outwardService.DockInAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/start")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> StartLoading(int id) =>
        await Handle(() => _outwardService.StartLoadingAsync(id, CurrentUserId));

    [HttpPost("{id:int}/photos")]
    [Authorize(Roles = "Supervisor")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<OutwardJobDto>> AddPhoto(int id, [FromForm] OutwardPhotoType type, IFormFile file, [FromForm] int? dispatchOrderLineId = null)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _outwardService.AddPhotoAsync(id, CurrentUserId, type, file.FileName, stream, dispatchOrderLineId));
    }

    [HttpPost("{id:int}/load-lines")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> SubmitLoadLines(int id, SubmitLoadLinesRequest request) =>
        await Handle(() => _outwardService.SubmitLoadLinesAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/lines")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> AddLine(int id, AddDispatchOrderLineRequest request) =>
        await Handle(() => _outwardService.AddDispatchOrderLineAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/exception")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> ReportException(int id, ReportOutwardExceptionRequest request) =>
        await Handle(() => _outwardService.ReportExceptionAsync(id, CurrentUserId, request));

    [HttpPost("{id:int}/confirm-dispatch-ready")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> ConfirmDispatchReady(int id) =>
        await Handle(() => _outwardService.ConfirmDispatchReadyAsync(id, CurrentUserId));

    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> Complete(int id) =>
        await Handle(() => _outwardService.CompleteAsync(id, CurrentUserId));

    [HttpPost("{id:int}/restart-loading")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<OutwardJobDto>> RestartLoading(int id) =>
        await Handle(() => _outwardService.RestartLoadingAsync(id, CurrentUserId));

    // ---------- 3D load plan: options ----------

    [HttpGet("{id:int}/load-plan/options")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadPlanOptionSummaryDto>>> GetLoadPlanOptions(int id) =>
        await Handle(() => _loadPlanService.GetOptionsAsync(id, CurrentUserId));

    [HttpPost("{id:int}/load-plan/options")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanOptionSummaryDto>> CreateLoadPlanOption(int id, CreateLoadPlanOptionRequest request) =>
        await Handle(() => _loadPlanService.CreateOptionAsync(id, CurrentUserId, request));

    [HttpDelete("{id:int}/load-plan/options/{optionId:int}")]
    [Authorize(Roles = "Supervisor")]
    public async Task<IActionResult> DeleteLoadPlanOption(int id, int optionId) =>
        await HandleNoContent(() => _loadPlanService.DeleteOptionAsync(id, optionId, CurrentUserId));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/select")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanOptionSummaryDto>> SelectLoadPlanOption(int id, int optionId) =>
        await Handle(() => _loadPlanService.SelectOptionAsync(id, optionId, CurrentUserId));

    // ---------- 3D load plan: groups ----------

    [HttpGet("{id:int}/load-plan/options/{optionId:int}/groups")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadPlanGroupDto>>> GetLoadPlanGroups(int id, int optionId) =>
        await Handle(() => _loadPlanService.GetGroupsAsync(id, optionId, CurrentUserId));

    [HttpGet("{id:int}/load-plan/options/{optionId:int}/groups/search")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadGroupSearchResultDto>>> SearchLoadPlanGroups(int id, int optionId, [FromQuery] string query = "") =>
        await Handle(() => _loadPlanService.SearchGroupsAsync(id, optionId, CurrentUserId, query));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/preview")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadGroupPreviewDto>> PreviewLoadPlanGroup(int id, int optionId, PlaceLoadGroupInZoneRequest request) =>
        await Handle(() => _loadPlanService.PreviewGroupAsync(id, optionId, CurrentUserId, request));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanGroupDto>> CreateLoadPlanGroup(int id, int optionId, PlaceLoadGroupInZoneRequest request) =>
        await Handle(() => _loadPlanService.CreateGroupAsync(id, optionId, CurrentUserId, request));

    [HttpPut("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanGroupDto>> UpdateLoadPlanGroup(int id, int optionId, int groupId, PlaceLoadGroupInZoneRequest request) =>
        await Handle(() => _loadPlanService.UpdateGroupAsync(id, optionId, groupId, CurrentUserId, request));

    [HttpDelete("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}")]
    [Authorize(Roles = "Supervisor")]
    public async Task<IActionResult> DeleteLoadPlanGroup(int id, int optionId, int groupId) =>
        await HandleNoContent(() => _loadPlanService.DeleteGroupAsync(id, optionId, groupId, CurrentUserId));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}/duplicate")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanGroupDto>> DuplicateLoadPlanGroup(int id, int optionId, int groupId) =>
        await Handle(() => _loadPlanService.DuplicateGroupAsync(id, optionId, groupId, CurrentUserId));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}/split")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadPlanGroupDto>>> SplitLoadPlanGroup(int id, int optionId, int groupId, SplitLoadGroupRequest request) =>
        await Handle(() => _loadPlanService.SplitGroupAsync(id, optionId, groupId, CurrentUserId, request.SplitQuantity));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/compact")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadPlanGroupDto>>> CompactLoadPlanGroups(int id, int optionId) =>
        await Handle(() => _loadPlanService.CompactGroupsAsync(id, optionId, CurrentUserId));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}/lock")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanGroupDto>> LockLoadPlanGroup(int id, int optionId, int groupId) =>
        await Handle(() => _loadPlanService.SetGroupLockAsync(id, optionId, groupId, CurrentUserId, locked: true));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/groups/{groupId:int}/unlock")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanGroupDto>> UnlockLoadPlanGroup(int id, int optionId, int groupId) =>
        await Handle(() => _loadPlanService.SetGroupLockAsync(id, optionId, groupId, CurrentUserId, locked: false));

    [HttpPost("{id:int}/load-plan/options/{optionId:int}/validate")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadPlanValidationDto>> ValidateLoadPlanOption(int id, int optionId) =>
        await Handle(() => _loadPlanService.ValidateOptionAsync(id, optionId, CurrentUserId));

    // ---------- 3D load plan: actual loading confirmation ----------

    [HttpPost("{id:int}/load-plan/confirm/start")]
    [Authorize(Roles = "Supervisor")]
    public async Task<IActionResult> StartLoadPlanConfirmation(int id) =>
        await HandleNoContent(() => _loadPlanService.StartConfirmationAsync(id, CurrentUserId));

    [HttpGet("{id:int}/load-plan/confirm/steps")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<List<LoadConfirmationStepDto>>> GetLoadPlanConfirmationSteps(int id) =>
        await Handle(() => _loadPlanService.GetConfirmationStepsAsync(id, CurrentUserId));

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/start")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadConfirmationStepDto>> StartLoadPlanGroup(int id, int groupId) =>
        await Handle(() => WithLiveUpdate(id, () => _loadPlanService.StartGroupAsync(id, groupId, CurrentUserId)));

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/loaded")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadConfirmationStepDto>> MarkLoadPlanGroupLoaded(int id, int groupId, ConfirmLoadGroupLoadedRequest request) =>
        await Handle(() => WithLiveUpdate(id, () => _loadPlanService.MarkGroupLoadedAsync(id, groupId, CurrentUserId, request)));

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/mismatch")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadConfirmationStepDto>> MarkLoadPlanGroupMismatch(int id, int groupId, ConfirmLoadGroupMismatchRequest request) =>
        await Handle(() => WithLiveUpdate(id, () => _loadPlanService.MarkGroupMismatchAsync(id, groupId, CurrentUserId, request)));

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/short-load")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadConfirmationStepDto>> MarkLoadPlanGroupShortLoad(int id, int groupId, ConfirmLoadGroupShortLoadRequest request) =>
        await Handle(() => WithLiveUpdate(id, () => _loadPlanService.MarkGroupShortLoadAsync(id, groupId, CurrentUserId, request)));

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/skip")]
    [Authorize(Roles = "Supervisor")]
    public async Task<ActionResult<LoadConfirmationStepDto>> SkipLoadPlanGroup(int id, int groupId, ConfirmLoadGroupSkipRequest request) =>
        await Handle(() => WithLiveUpdate(id, () => _loadPlanService.SkipGroupAsync(id, groupId, CurrentUserId, request)));

    // Group-confirmation actions change LoadingCompletionProgress - push the refreshed
    // job to every connected Supervisor/Security client so home-page cards update live,
    // not just the next time the acting supervisor's page happens to reload.
    private async Task<T> WithLiveUpdate<T>(int id, Func<Task<T>> action)
    {
        var result = await action();
        await _outwardService.PushUpdateAsync(id);
        return result;
    }

    [HttpPost("{id:int}/load-plan/confirm/groups/{groupId:int}/photo")]
    [Authorize(Roles = "Supervisor")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<LoadConfirmationStepDto>> AddLoadPlanGroupPhoto(int id, int groupId, IFormFile file)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { message = "File is empty." });
        }

        await using var stream = file.OpenReadStream();
        return await Handle(() => _loadPlanService.AddGroupPhotoAsync(id, groupId, CurrentUserId, file.FileName, stream));
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

    private async Task<IActionResult> HandleNoContent(Func<Task> action)
    {
        try
        {
            await action();
            return NoContent();
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

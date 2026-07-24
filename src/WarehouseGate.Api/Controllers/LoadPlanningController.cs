using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/load-planning")]
[Authorize(Roles = "Supervisor")]
public class LoadPlanningController : ControllerBase
{
    private readonly LoadPlanningService _loadPlanningService;

    public LoadPlanningController(LoadPlanningService loadPlanningService)
    {
        _loadPlanningService = loadPlanningService;
    }

    [HttpGet("vehicle-profiles")]
    public async Task<ActionResult<List<VehicleProfileDto>>> GetVehicleProfiles() =>
        Ok(await _loadPlanningService.GetVehicleProfilesAsync());

    [HttpPost("optimize")]
    public async Task<ActionResult<LoadPlanResultDto>> Optimize(OptimizeLoadPlanRequest request)
    {
        try
        {
            return Ok(await _loadPlanningService.OptimizeAsync(request));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

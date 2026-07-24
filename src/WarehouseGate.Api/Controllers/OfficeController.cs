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
[Route("api/office")]
[Authorize(Roles = "Office")]
public class OfficeController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly OutwardService _outwardService;
    private readonly InwardService _inwardService;
    private readonly AuditService _audit;
    private readonly IHubContext<InwardHub> _hub;

    public OfficeController(WarehouseGateDbContext db, OutwardService outwardService, InwardService inwardService, AuditService audit, IHubContext<InwardHub> hub)
    {
        _db = db;
        _outwardService = outwardService;
        _inwardService = inwardService;
        _audit = audit;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private string CurrentUserName => User.FindFirstValue("displayName") ?? User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

    private async Task<int?> GetCallerWarehouseIdAsync() =>
        await _db.Users.Where(u => u.Id == CurrentUserId).Select(u => u.WarehouseId).FirstOrDefaultAsync();

    // ============================ DISPATCH PLAN BRIDGE (warehouse-scoped) ============================
    // Office's view into the Logistics Manager's Dispatch Plan upload: rows where From = this
    // warehouse are pending outward work (generate a pick list from them); rows where To = this
    // warehouse are pending inward arrivals (read-only visibility until Security gates them in).

    [HttpGet("dispatch-plan/outward/pending")]
    public async Task<ActionResult<List<PendingDispatchPlanGroupDto>>> GetPendingOutwardDispatchPlan()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<PendingDispatchPlanGroupDto>());
        }

        return Ok(await GetPendingGroupsAsync(fromWarehouseId: warehouseId.Value, toWarehouseId: null));
    }

    [HttpPost("dispatch-plan/outward/{vehicleNumber}/generate-picklist")]
    public async Task<ActionResult<OutwardJobDto>> GeneratePickListFromDispatchPlan(string vehicleNumber)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        try
        {
            var job = await _outwardService.GeneratePickListFromDispatchPlanAsync(vehicleNumber, warehouseId.Value, CurrentUserId);
            await _audit.LogAsync("OutwardTransaction", job.Id, AuditAction.Created,
                $"Pick list generated for vehicle '{vehicleNumber}' from Dispatch Plan data.", CurrentUserId, CurrentUserName);
            return Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Office's own override of how many boxes to actually pick for one Dispatch Plan line, kept
    // on a separate column from the Logistics Manager's planned BoxQuantity (see PickListQuantity's
    // doc comment on the domain model). Only allowed on this Office's own outbound rows that
    // haven't been claimed by a pick-list generation yet.
    [HttpPut("dispatch-plan/outward/lines/{id:int}/picklist-quantity")]
    public async Task<IActionResult> UpdatePickListQuantity(int id, UpdatePickListQuantityRequest request)
    {
        if (request.Quantity is < 1)
        {
            return BadRequest(new { message = "Pick list quantity must be at least 1." });
        }

        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        var record = await _db.VehicleLogisticsRecords.FindAsync(id);
        if (record is null)
        {
            return NotFound();
        }

        if (record.FromWarehouseId != warehouseId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This row is not outbound from your warehouse." });
        }

        if (record.Status != VehicleLogisticsStatus.InTransit)
        {
            return BadRequest(new { message = "This vehicle's pick list has already been generated - quantities can no longer be adjusted here." });
        }

        record.PickListQuantity = request.Quantity;
        record.UpdatedByUserId = CurrentUserId;
        record.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.LogisticsGroup, InwardHub.AdminsGroup).SendAsync("VehicleLogisticsRecordChanged");
        return NoContent();
    }

    [HttpGet("dispatch-plan/inward/pending")]
    public async Task<ActionResult<List<PendingDispatchPlanGroupDto>>> GetPendingInwardDispatchPlan()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<PendingDispatchPlanGroupDto>());
        }

        return Ok(await GetPendingGroupsAsync(fromWarehouseId: null, toWarehouseId: warehouseId.Value));
    }

    private async Task<List<PendingDispatchPlanGroupDto>> GetPendingGroupsAsync(int? fromWarehouseId, int? toWarehouseId)
    {
        var query = _db.VehicleLogisticsRecords
            .Include(r => r.FromWarehouse).Include(r => r.ToWarehouse)
            .AsQueryable();

        query = fromWarehouseId is not null
            // Outward-pending: only rows this Office hasn't actioned yet (no pick list
            // generated). Once claimed, Status flips to InProgress and the row drops off this
            // list - Office already has the resulting Outward job to work from instead.
            ? query.Where(r => r.FromWarehouseId == fromWarehouseId && r.Status == VehicleLogisticsStatus.InTransit)
            // Inward-pending ("Expected, not yet arrived"): still relevant even after the
            // source warehouse's Outward job has claimed and dispatched it (Status flips
            // InTransit -> InProgress at that point) - the shipment is now physically in
            // transit toward here, which is exactly what this list exists to show. It only
            // drops off once Security here actually gates it in (ConsumedByInwardTransactionId
            // gets set).
            : query.Where(r => r.ToWarehouseId == toWarehouseId
                && r.ConsumedByInwardTransactionId == null
                && (r.Status == VehicleLogisticsStatus.InTransit
                    || (r.Status == VehicleLogisticsStatus.InProgress && r.ConsumedByOutwardTransactionId != null)));

        var rows = await query.ToListAsync();

        return rows
            .GroupBy(r => r.VehicleNumber)
            .Select(g => new PendingDispatchPlanGroupDto(
                g.Key,
                fromWarehouseId is not null ? g.First().ToWarehouse!.Name : g.First().FromWarehouse!.Name,
                g.Where(r => r.EtaDateTime.HasValue).Select(r => r.EtaDateTime).OrderBy(d => d).FirstOrDefault(),
                g.Select(r => new PendingDispatchPlanLineDto(r.Id, r.Sku, r.SkuCode, r.BoxQuantity, r.PickListQuantity, r.PoNumber)).ToList()))
            .OrderBy(g => g.EtaDateTime)
            .ToList();
    }

    // ============================ FOLLOW-UPS (warehouse-scoped) ============================
    // Actionable to-dos created automatically when completed jobs need human follow-up
    // (exception GRNs, partial-load dispatch notes) - see FollowUpTask.

    private const int MaxFollowUpResults = 200;

    [HttpGet("follow-ups")]
    public async Task<ActionResult<List<FollowUpTaskDto>>> GetFollowUps()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<FollowUpTaskDto>());
        }

        var tasks = await _db.FollowUpTasks
            .Where(t => t.WarehouseId == warehouseId)
            .OrderBy(t => t.Status)               // Open (0) before Resolved (1)
            .ThenByDescending(t => t.CreatedAtUtc)
            .Take(MaxFollowUpResults)
            .ToListAsync();

        return Ok(tasks.Select(ToDto).ToList());
    }

    [HttpPost("follow-ups/{id:int}/resolve")]
    public async Task<ActionResult<FollowUpTaskDto>> ResolveFollowUp(int id, ResolveFollowUpRequest request)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        var task = await _db.FollowUpTasks.FindAsync(id);
        if (task is null)
        {
            return NotFound();
        }

        if (task.WarehouseId != warehouseId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This follow-up is not in your warehouse." });
        }

        if (task.Status == FollowUpStatus.Resolved)
        {
            return BadRequest(new { message = "This follow-up has already been resolved." });
        }

        task.Status = FollowUpStatus.Resolved;
        task.ResolvedByUserId = CurrentUserId;
        task.ResolvedByName = CurrentUserName;
        task.ResolvedAtUtc = DateTime.UtcNow;
        task.ResolutionNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await _db.SaveChangesAsync();

        await _audit.LogAsync("FollowUpTask", task.Id, AuditAction.Updated,
            $"Follow-up resolved: {task.Title}{(task.ResolutionNotes is null ? "" : $" - {task.ResolutionNotes}")}", CurrentUserId, CurrentUserName);
        await _hub.Clients.Groups(InwardHub.OfficeGroup, InwardHub.AdminsGroup).SendAsync("FollowUpsChanged");

        return Ok(ToDto(task));
    }

    private static FollowUpTaskDto ToDto(FollowUpTask t) => new(
        t.Id, t.Type.ToString(), t.Status.ToString(), t.EntityName, t.EntityId,
        t.Title, t.Details, t.CreatedAtUtc, t.ResolvedByName, t.ResolvedAtUtc, t.ResolutionNotes);

    // ============================ INWARD JOBS (warehouse-scoped) ============================

    [HttpGet("inward-jobs")]
    public async Task<ActionResult<List<InwardJobDto>>> GetInwardJobs([FromQuery] bool activeOnly = false, [FromQuery] string? vehicleNumber = null)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<InwardJobDto>());
        }

        return Ok(await _inwardService.GetForOfficeAsync(warehouseId.Value, activeOnly, vehicleNumber));
    }

    [HttpGet("inward-jobs/{id:int}")]
    public async Task<ActionResult<InwardJobDto>> GetInwardJob(int id)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        var job = await _inwardService.GetByIdForOfficeAsync(id, warehouseId.Value);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("supervisors")]
    public async Task<ActionResult<List<SupervisorOptionDto>>> GetSupervisors()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<SupervisorOptionDto>());
        }

        var supervisors = await _db.Users
            .Where(u => u.Role == UserRole.Supervisor && u.WarehouseId == warehouseId)
            .OrderBy(u => u.DisplayName)
            .Select(u => new SupervisorOptionDto(u.Id, u.DisplayName))
            .ToListAsync();

        return Ok(supervisors);
    }

    [HttpPost("inward-jobs/{id:int}/assign")]
    public async Task<ActionResult<InwardJobDto>> AssignSupervisor(int id, AssignSupervisorRequest request)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        try
        {
            var job = await _inwardService.AssignSupervisorAsync(id, request.SupervisorUserId, warehouseId.Value);

            var supervisorName = await _db.Users.Where(u => u.Id == request.SupervisorUserId).Select(u => u.DisplayName).FirstOrDefaultAsync();
            await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
                $"Assigned {supervisorName} to vehicle {job.VehicleNumber}.", CurrentUserId, CurrentUserName);

            return Ok(job);
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

    [HttpPut("inward-jobs/{id:int}")]
    public async Task<ActionResult<InwardJobDto>> UpdateInwardJob(int id, UpdateInwardOfficeFieldsRequest request)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        try
        {
            var job = await _inwardService.UpdateOfficeFieldsAsync(id, request, warehouseId.Value);
            await _audit.LogAsync("InwardTransaction", id, AuditAction.Updated,
                $"Inward record for '{job.VehicleNumber}' updated by Office.", CurrentUserId, CurrentUserName);
            return Ok(job);
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

    // ============================ OUTWARD JOBS (warehouse-scoped) ============================
    // Mirrors the INWARD JOBS region above exactly - same scoping, same assign flow, same audit
    // pattern - so Office staff get identical supervisor-assignment capability for Outward.

    [HttpGet("outward-jobs")]
    public async Task<ActionResult<List<OutwardJobDto>>> GetOutwardJobs([FromQuery] bool activeOnly = false, [FromQuery] string? vehicleNumber = null)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<OutwardJobDto>());
        }

        return Ok(await _outwardService.GetForOfficeAsync(warehouseId.Value, activeOnly, vehicleNumber));
    }

    [HttpGet("outward-jobs/{id:int}")]
    public async Task<ActionResult<OutwardJobDto>> GetOutwardJob(int id)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        var job = await _outwardService.GetByIdForOfficeAsync(id, warehouseId.Value);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost("outward-jobs/{id:int}/assign")]
    public async Task<ActionResult<OutwardJobDto>> AssignOutwardSupervisor(int id, AssignSupervisorRequest request)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        try
        {
            var job = await _outwardService.AssignSupervisorAsync(id, request.SupervisorUserId, warehouseId.Value);

            var supervisorName = await _db.Users.Where(u => u.Id == request.SupervisorUserId).Select(u => u.DisplayName).FirstOrDefaultAsync();
            await _audit.LogAsync("OutwardTransaction", id, AuditAction.Updated,
                $"Assigned {supervisorName} to vehicle {job.VehicleNumber}.", CurrentUserId, CurrentUserName);

            return Ok(job);
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

    [HttpGet("audit-log")]
    public async Task<ActionResult<List<OfficeAuditLogDto>>> GetAuditLog()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<OfficeAuditLogDto>());
        }

        var inwardIdsInWarehouse = _db.InwardTransactions
            .Where(t => t.WarehouseId == warehouseId)
            .Select(t => t.Id);
        var outwardIdsInWarehouse = _db.OutwardTransactions
            .Where(t => t.WarehouseId == warehouseId)
            .Select(t => t.Id);

        var logs = await _db.AuditLogs
            .Where(a =>
                (a.EntityName == "InwardTransaction" && inwardIdsInWarehouse.Contains(a.EntityId)) ||
                (a.EntityName == "OutwardTransaction" && outwardIdsInWarehouse.Contains(a.EntityId)))
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(200)
            .ToListAsync();

        return Ok(logs.Select(a => new OfficeAuditLogDto(
            a.Id, a.EntityName, a.EntityId, a.Action.ToString(), a.ChangedByName, a.ChangedAtUtc, a.Summary)).ToList());
    }
}

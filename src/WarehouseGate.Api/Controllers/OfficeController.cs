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

    [HttpPost("dispatch-plan/outward/{poNumber}/generate-picklist")]
    public async Task<ActionResult<OutwardJobDto>> GeneratePickListFromDispatchPlan(string poNumber)
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return NotFound();
        }

        try
        {
            var job = await _outwardService.GeneratePickListFromDispatchPlanAsync(poNumber, warehouseId.Value, CurrentUserId);
            await _audit.LogAsync("OutwardTransaction", job.Id, AuditAction.Created,
                $"Pick list generated for PO '{poNumber}' from Dispatch Plan data.", CurrentUserId, CurrentUserName);
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

    // Attaches an already-gated-in (Security-created) Inward Job to a real Dispatch Plan entry,
    // picked from the Expected tab - moves it from "unlinked" into a real, inspectable job. See
    // InwardService.LinkVehicleAsync for the full flow.
    [HttpPost("dispatch-plan/inward/link-vehicle")]
    public async Task<ActionResult<InwardJobDto>> LinkVehicle(LinkVehicleRequest request)
    {
        try
        {
            return Ok(await _inwardService.LinkVehicleAsync(request, CurrentUserId));
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

    // Backs the vehicle dropdown on the Link Vehicle dialog - Inward Jobs Security has already
    // gated in at this warehouse but that aren't linked to any Dispatch Plan entry yet.
    [HttpGet("dispatch-plan/inward/unlinked-arrivals")]
    public async Task<ActionResult<List<UnlinkedArrivalDto>>> GetUnlinkedArrivals()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<UnlinkedArrivalDto>());
        }

        var arrivals = await _db.InwardTransactions
            .Include(t => t.Vehicle)
            .Where(t => t.WarehouseId == warehouseId && t.PurchaseOrderId == null)
            .OrderByDescending(t => t.GateInTime)
            .ToListAsync();

        return Ok(arrivals.Select(t => new UnlinkedArrivalDto(
            t.Id, t.Vehicle!.Number, t.DriverName, t.DriverMobile, t.TransporterName, t.SecurityEnteredPoNumber, t.GateInTime)).ToList());
    }

    // Attaches an already-gated-in (Security-created) outward arrival to a pick list Office already
    // generated, picked from the Outward Jobs list - moves it from "not yet assigned" into a real
    // job with vehicle/driver/photos. See OutwardService.LinkVehicleAsync for the full flow.
    [HttpPost("dispatch-plan/outward/link-vehicle")]
    public async Task<ActionResult<OutwardJobDto>> LinkOutwardVehicle(LinkOutwardVehicleRequest request)
    {
        try
        {
            return Ok(await _outwardService.LinkVehicleAsync(request, CurrentUserId));
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

    // Backs the vehicle dropdown on the Link Vehicle dialog - outward arrivals Security has already
    // gated in at this warehouse but that aren't linked to any pick list yet.
    [HttpGet("dispatch-plan/outward/unlinked-arrivals")]
    public async Task<ActionResult<List<OutwardGateArrivalDto>>> GetUnlinkedOutwardArrivals()
    {
        var warehouseId = await GetCallerWarehouseIdAsync();
        if (warehouseId is null)
        {
            return Ok(new List<OutwardGateArrivalDto>());
        }

        return Ok(await _outwardService.GetUnlinkedArrivalsAsync(warehouseId.Value));
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

        // Both Outward (source-side pick-list generation) and Inward ("Expected, not yet arrived")
        // group by PO Number - vehicle number is normally unknown this early (Security supplies it
        // independently at gate-in and Office links it afterward, see OutwardService.LinkVehicleAsync
        // and InwardService.LinkVehicleAsync), so PO Number is the only identity guaranteed present
        // before that happens.
        if (fromWarehouseId is not null)
        {
            return rows
                .GroupBy(r => r.PoNumber ?? $"__no_po_{r.Id}")
                .Select(g => new PendingDispatchPlanGroupDto(
                    g.First().VehicleNumber,
                    g.First().PoNumber,
                    g.First().ToWarehouse!.Name,
                    g.Where(r => r.EtaDateTime.HasValue).Select(r => r.EtaDateTime).OrderBy(d => d).FirstOrDefault(),
                    g.Select(r => new PendingDispatchPlanLineDto(r.Id, r.Sku, r.SkuCode, r.BoxQuantity, r.PickListQuantity, r.PoNumber)).ToList()))
                .OrderBy(g => g.EtaDateTime)
                .ToList();
        }

        return rows
            .GroupBy(r => r.PoNumber ?? $"__nopo_{r.Id}")
            .Select(g => new PendingDispatchPlanGroupDto(
                g.First().VehicleNumber,
                g.Key,
                g.First().FromWarehouse!.Name,
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

    // Lets Office correct the Supervisor's recorded quantities and Mismatch SKU Details rows
    // before confirming - reuses the exact same request shape and validation as Supervisor's
    // original submission (InwardService.SubmitInspectionAsync), just callable while
    // PendingOfficeVerification instead of Docked/Inspecting. See InwardService.UpdateInspectionAsync.
    [HttpPut("inward-jobs/{id:int}/inspection")]
    public async Task<ActionResult<InwardJobDto>> UpdateInspection(int id, SubmitInspectionRequest request)
    {
        try
        {
            var job = await _inwardService.UpdateInspectionAsync(id, CurrentUserId, request);
            return Ok(job);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Office's final confirmation before a GRN exists - Supervisor's "Complete Unloading" (see
    // InwardService.CompleteAsync) only gets a job to PendingOfficeVerification; this is what
    // actually generates the GoodsReceiptNote (InwardService.VerifyAndGenerateGrnAsync).
    [HttpPost("inward-jobs/{id:int}/verify-and-generate-grn")]
    public async Task<ActionResult<InwardJobDto>> VerifyAndGenerateGrn(int id)
    {
        try
        {
            var job = await _inwardService.VerifyAndGenerateGrnAsync(id, CurrentUserId);
            return Ok(job);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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

    // Office's completion gate - reviews Ordered/Pick List/Loaded quantities (already surfaced on
    // the web job detail page) and confirms, finalizing the job and generating its dispatch note.
    // Mirrors VerifyAndGenerateGrn below exactly.
    [HttpPost("outward-jobs/{id:int}/verify-and-complete")]
    public async Task<ActionResult<OutwardJobDto>> VerifyAndCompleteOutwardJob(int id)
    {
        try
        {
            var job = await _outwardService.VerifyAndCompleteAsync(id, CurrentUserId);
            return Ok(job);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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

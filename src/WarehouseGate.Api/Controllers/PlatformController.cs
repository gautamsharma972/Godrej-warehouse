using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

// The multi-tenant site's own API: create/manage Organizations and their user/warehouse limits,
// and administer users across every organization (list + password reset). PlatformAdmin accounts
// have a null OrganizationId, so every query here deliberately uses IgnoreQueryFilters() - the
// one controller in the app that's meant to see across tenant boundaries.
[ApiController]
[Route("api/platform")]
[Authorize(Roles = "PlatformAdmin")]
public class PlatformController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public PlatformController(WarehouseGateDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // ============================ ORGANIZATIONS ============================

    [HttpGet("organizations")]
    public async Task<ActionResult<List<OrganizationDto>>> GetOrganizations()
    {
        var orgs = await _db.Organizations.OrderBy(o => o.Name).ToListAsync();

        var userCounts = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.OrganizationId != null)
            .GroupBy(u => u.OrganizationId!.Value)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.OrganizationId, g => g.Count);

        var warehouseCounts = await _db.Warehouses.IgnoreQueryFilters()
            .GroupBy(w => w.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.OrganizationId, g => g.Count);

        return Ok(orgs.Select(o => new OrganizationDto(
            o.Id, o.Name, o.Code, o.IsActive, o.MaxUsers, o.MaxWarehouses,
            userCounts.GetValueOrDefault(o.Id),
            warehouseCounts.GetValueOrDefault(o.Id),
            o.CreatedAtUtc)).ToList());
    }

    [HttpPost("organizations")]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization(CreateOrganizationRequest request)
    {
        var name = request.Name.Trim();
        var code = request.Code.Trim().ToUpperInvariant();
        var adminUserName = request.AdminUserName.Trim();
        var adminDisplayName = request.AdminDisplayName.Trim();

        if (name.Length == 0 || code.Length == 0)
        {
            return BadRequest(new { message = "Organization name and code are required." });
        }
        if (adminUserName.Length == 0 || adminDisplayName.Length == 0 || request.AdminPassword.Length == 0)
        {
            return BadRequest(new { message = "The organization's initial SuperAdmin username, display name, and password are required." });
        }
        if (await _db.Organizations.AnyAsync(o => o.Code == code))
        {
            return BadRequest(new { message = $"Organization code '{code}' is already in use." });
        }

        var org = new Organization
        {
            Name = name,
            Code = code,
            IsActive = true,
            MaxUsers = request.MaxUsers,
            MaxWarehouses = request.MaxWarehouses,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        var admin = new ApplicationUser
        {
            UserName = adminUserName,
            Email = $"{adminUserName}@warehousegate.local",
            DisplayName = adminDisplayName,
            Role = UserRole.SuperAdmin,
            OrganizationId = org.Id,
            EmailConfirmed = true
        };
        var result = await _userManager.CreateAsync(admin, request.AdminPassword);
        if (!result.Succeeded)
        {
            // Roll back the org so a failed admin-password (e.g. too short) doesn't leave an
            // orphaned, admin-less organization behind for the caller to retry against.
            _db.Organizations.Remove(org);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        return await GetOrganizationDtoAsync(org.Id);
    }

    [HttpPut("organizations/{id:int}")]
    public async Task<ActionResult<OrganizationDto>> UpdateOrganization(int id, UpdateOrganizationRequest request)
    {
        var org = await _db.Organizations.FindAsync(id);
        if (org is null)
        {
            return NotFound();
        }

        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            return BadRequest(new { message = "Organization name is required." });
        }

        org.Name = name;
        org.IsActive = request.IsActive;
        org.MaxUsers = request.MaxUsers;
        org.MaxWarehouses = request.MaxWarehouses;
        await _db.SaveChangesAsync();

        return await GetOrganizationDtoAsync(org.Id);
    }

    private async Task<ActionResult<OrganizationDto>> GetOrganizationDtoAsync(int id)
    {
        var org = await _db.Organizations.FirstAsync(o => o.Id == id);
        var userCount = await _db.Users.IgnoreQueryFilters().CountAsync(u => u.OrganizationId == id);
        var warehouseCount = await _db.Warehouses.IgnoreQueryFilters().CountAsync(w => w.OrganizationId == id);
        return Ok(new OrganizationDto(org.Id, org.Name, org.Code, org.IsActive, org.MaxUsers, org.MaxWarehouses, userCount, warehouseCount, org.CreatedAtUtc));
    }

    // ============================ USERS (across every organization) ============================

    [HttpGet("users")]
    public async Task<ActionResult<List<PlatformUserDto>>> GetUsers()
    {
        var users = await _db.Users.IgnoreQueryFilters()
            .Include(u => u.Organization)
            .OrderBy(u => u.Organization == null ? "" : u.Organization.Name).ThenBy(u => u.UserName)
            .ToListAsync();

        return Ok(users.Select(u => new PlatformUserDto(
            u.Id, u.UserName!, u.DisplayName, u.Role.ToString(), u.OrganizationId, u.Organization?.Name ?? "Platform")).ToList());
    }

    [HttpPost("users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { message = "A new password is required." });
        }

        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        return NoContent();
    }
}

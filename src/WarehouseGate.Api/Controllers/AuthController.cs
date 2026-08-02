using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Api.Services;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;
    private readonly WarehouseGateDbContext _db;

    public AuthController(UserManager<ApplicationUser> userManager, TokenService tokenService, WarehouseGateDbContext db)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _db = db;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var normalizedUserName = request.UserName.Trim().ToUpperInvariant();
        ApplicationUser? user;

        if (!string.IsNullOrWhiteSpace(request.OrganizationCode))
        {
            var code = request.OrganizationCode.Trim().ToUpperInvariant();
            var org = await _db.Organizations.FirstOrDefaultAsync(o => o.IsActive && o.Code.ToUpper() == code);
            if (org is null)
            {
                return Unauthorized(new { message = "Invalid organization code, username, or password." });
            }

            // IgnoreQueryFilters: the caller's own organization isn't known yet - that's exactly
            // what this lookup is resolving - so the usual tenant query filter can't apply here.
            user = await _db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.OrganizationId == org.Id && u.NormalizedUserName == normalizedUserName);
        }
        else
        {
            // No org code given (e.g. the mobile app, which predates multi-tenancy) - fall back to
            // a global username lookup. Succeeds only while that username is unambiguous; if two
            // organizations ever end up with the same username, this asks the caller to disambiguate
            // with an Organization Code instead of guessing which account was meant.
            var matches = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.NormalizedUserName == normalizedUserName)
                .ToListAsync();

            if (matches.Count > 1)
            {
                return Unauthorized(new { message = "This username exists in more than one organization - sign in with an Organization Code." });
            }

            user = matches.SingleOrDefault();
        }

        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var (token, expiresAtUtc) = _tokenService.CreateToken(user);
        var scope = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.Id == user.Id)
            .Select(u => new
            {
                WarehouseName = u.Warehouse == null ? null : u.Warehouse.Name,
                RegionName = u.Region == null ? null : u.Region.Name,
                OrganizationName = u.Organization == null ? null : u.Organization.Name
            })
            .FirstAsync();

        return Ok(new LoginResponse(
            token,
            user.Role.ToString(),
            user.DisplayName,
            expiresAtUtc,
            scope.WarehouseName,
            scope.RegionName,
            scope.OrganizationName));
    }
}

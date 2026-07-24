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
        var user = await _userManager.FindByNameAsync(request.UserName);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var (token, expiresAtUtc) = _tokenService.CreateToken(user);
        var scope = await _db.Users
            .Where(u => u.Id == user.Id)
            .Select(u => new
            {
                WarehouseName = u.Warehouse == null ? null : u.Warehouse.Name,
                RegionName = u.Region == null ? null : u.Region.Name
            })
            .FirstAsync();

        return Ok(new LoginResponse(
            token,
            user.Role.ToString(),
            user.DisplayName,
            expiresAtUtc,
            scope.WarehouseName,
            scope.RegionName));
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Controllers;

// Active dock bays for the CALLER'S own warehouse - feeds the mobile dock-in bay picker.
// An empty list means this warehouse hasn't defined a bay master yet, and the mobile app
// falls back to its legacy free-number entry.
[ApiController]
[Route("api/bays")]
[Authorize]
public class BaysController : ControllerBase
{
    private readonly WarehouseGateDbContext _db;

    public BaysController(WarehouseGateDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<string>>> GetMyWarehouseBays()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var warehouseId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.WarehouseId)
            .FirstOrDefaultAsync();

        if (warehouseId is null)
        {
            return Ok(new List<string>());
        }

        var bays = await _db.DockBays
            .Where(b => b.WarehouseId == warehouseId && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => b.Name)
            .ToListAsync();

        return Ok(bays);
    }
}

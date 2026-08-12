using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Infrastructure.Storage;

namespace WarehouseGate.Api.Controllers;

// Serves back whatever IPhotoStorageService wrote, however it wrote it - storage-agnostic by
// design, so switching the registered implementation (local disk <-> S3) in Program.cs is the only
// change needed anywhere in the app. Any authenticated role can read - both Security and Supervisor
// need to view evidence photos, and the write side (GateController/InwardController/
// OutwardController) already gates who can upload in the first place.
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IPhotoStorageService _photoStorage;

    public FilesController(IPhotoStorageService photoStorage)
    {
        _photoStorage = photoStorage;
    }

    [HttpGet("{**relativePath}")]
    public async Task<IActionResult> Get(string relativePath, CancellationToken ct)
    {
        var result = await _photoStorage.GetForServingAsync(relativePath, ct);
        if (!result.Found)
        {
            return NotFound();
        }

        if (result.RedirectUrl is not null)
        {
            return Redirect(result.RedirectUrl);
        }

        return File(result.Content!, result.ContentType!);
    }
}

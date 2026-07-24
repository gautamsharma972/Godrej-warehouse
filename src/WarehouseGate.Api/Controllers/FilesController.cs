using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WarehouseGate.Infrastructure.Storage;

namespace WarehouseGate.Api.Controllers;

// Serves back whatever IPhotoStorageService/LocalDiskPhotoStorageService already wrote to disk.
// Any authenticated role can read - both Security and Supervisor need to view evidence photos,
// and the write side (GateController/InwardController/OutwardController) already gates who can
// upload in the first place.
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly LocalDiskPhotoStorageOptions _options;

    public FilesController(LocalDiskPhotoStorageOptions options)
    {
        _options = options;
    }

    [HttpGet("{**relativePath}")]
    public IActionResult Get(string relativePath)
    {
        var root = Path.GetFullPath(_options.RootPath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        // The resolved path must stay inside the photo storage root - blocks "../../" traversal.
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return PhysicalFile(fullPath, contentType);
    }
}

namespace WarehouseGate.Infrastructure.Storage;

public class LocalDiskPhotoStorageOptions
{
    public string RootPath { get; set; } = "App_Data/photos";
}

public class LocalDiskPhotoStorageService : IPhotoStorageService
{
    private readonly LocalDiskPhotoStorageOptions _options;

    public LocalDiskPhotoStorageService(LocalDiskPhotoStorageOptions options)
    {
        _options = options;
    }

    public async Task<string> SaveAsync(string transactionKey, string fileName, Stream content, CancellationToken ct = default)
    {
        var transactionFolder = Path.Combine(_options.RootPath, transactionKey);
        Directory.CreateDirectory(transactionFolder);

        var safeFileName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var fullPath = Path.Combine(transactionFolder, safeFileName);

        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, ct);
        }

        return Path.Combine(transactionKey, safeFileName).Replace('\\', '/');
    }

    public Task<FileServeResult> GetForServingAsync(string relativePath, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(_options.RootPath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));

        // The resolved path must stay inside the photo storage root - blocks "../../" traversal.
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return Task.FromResult(FileServeResult.NotFound);
        }

        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return Task.FromResult(FileServeResult.Stream(File.OpenRead(fullPath), contentType));
    }
}

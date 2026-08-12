namespace WarehouseGate.Infrastructure.Storage;

// Whichever implementation is active owns BOTH how a file is written and how it's handed back to a
// client - FilesController just calls GetForServingAsync and acts on whatever shape comes back,
// with no knowledge of disk paths or S3 keys. Local disk hands back bytes to stream; S3 hands back
// a short-lived presigned URL to redirect to, so file traffic goes straight to S3 instead of
// bouncing through this API's own bandwidth.
public interface IPhotoStorageService
{
    Task<string> SaveAsync(string transactionKey, string fileName, Stream content, CancellationToken ct = default);

    Task<FileServeResult> GetForServingAsync(string relativePath, CancellationToken ct = default);
}

public sealed class FileServeResult
{
    public string? RedirectUrl { get; private init; }
    public Stream? Content { get; private init; }
    public string? ContentType { get; private init; }
    public bool Found { get; private init; }

    public static FileServeResult NotFound { get; } = new() { Found = false };

    public static FileServeResult Redirect(string url) => new() { Found = true, RedirectUrl = url };

    public static FileServeResult Stream(System.IO.Stream content, string contentType) =>
        new() { Found = true, Content = content, ContentType = contentType };
}

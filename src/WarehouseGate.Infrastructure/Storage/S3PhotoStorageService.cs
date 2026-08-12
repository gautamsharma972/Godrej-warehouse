using Amazon.S3;
using Amazon.S3.Model;

namespace WarehouseGate.Infrastructure.Storage;

public class S3PhotoStorageOptions
{
    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
}

// Same key layout as LocalDiskPhotoStorageService ("{transactionKey}/{guid}.{ext}") so the FilePath
// values this writes are indistinguishable from disk-era ones to every caller - InwardService/
// OutwardService/OutwardLoadPlanService and the mobile/web clients all just treat it as an opaque
// relative string. IAmazonS3 itself carries no explicit credentials - the AWS SDK's default
// credential chain resolves them (EC2/ECS instance role in production, env vars/profile locally),
// so no access key ever needs to live in this codebase or its config.
public class S3PhotoStorageService : IPhotoStorageService
{
    // Long enough that a slow connection or a page left open doesn't expire mid-view, short enough
    // that a leaked/cached link doesn't stay live indefinitely.
    private static readonly TimeSpan PresignedUrlLifetime = TimeSpan.FromMinutes(15);

    private readonly IAmazonS3 _s3;
    private readonly S3PhotoStorageOptions _options;

    public S3PhotoStorageService(IAmazonS3 s3, S3PhotoStorageOptions options)
    {
        _s3 = s3;
        _options = options;
    }

    public async Task<string> SaveAsync(string transactionKey, string fileName, Stream content, CancellationToken ct = default)
    {
        var safeFileName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var key = $"{transactionKey}/{safeFileName}";

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = ContentTypeFor(fileName)
        };
        await _s3.PutObjectAsync(request, ct);

        return key;
    }

    // No HeadObject existence check before redirecting - that would cost a full extra S3 round trip
    // on every single photo view just to guard a case the client already handles fine: if the key
    // genuinely doesn't exist, S3 itself returns 404 the moment the browser/app follows the URL.
    public Task<FileServeResult> GetForServingAsync(string relativePath, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = relativePath,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(PresignedUrlLifetime)
        };

        var url = _s3.GetPreSignedURL(request);
        return Task.FromResult(FileServeResult.Redirect(url));
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };
}

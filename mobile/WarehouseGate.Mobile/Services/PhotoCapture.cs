using Microsoft.Maui.Graphics.Platform;

namespace WarehouseGate.Mobile.Services;

// Every photo-capture call site funnels through here so captured photos are consistently
// downsampled before they ever touch disk. Raw camera captures (often 12MP+, tens of MB once
// decoded into an in-memory bitmap) were the cause of intermittent OutOfMemory crashes once a
// few photos piled up in a thumbnail gallery or the full-screen viewer - MAUI's Image control
// decodes ImageSource.FromFile at full resolution regardless of how small it's displayed.
// Downsampling once at capture time (instead of at every render) also shrinks upload payloads.
public static class PhotoCapture
{
    private const float MaxDimension = 1600f;
    private const float JpegQuality = 0.85f;

    // Null means "nothing to save" - either capture isn't supported on this device or the user
    // cancelled. Callers that want a specific "not supported" message check
    // MediaPicker.Default.IsCaptureSupported themselves before calling this.
    public static async Task<string?> CaptureAndSaveAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            return null;
        }

        var photo = await MediaPicker.Default.CapturePhotoAsync();
        return photo is null ? null : await SaveResizedAsync(photo);
    }

    private static async Task<string> SaveResizedAsync(FileResult photo)
    {
        var localPath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid():N}.jpg");

        // Decoding + re-encoding is real CPU work - keep it off the UI thread so the capture
        // flow's spinner stays responsive instead of the whole page freezing mid-resize.
        await Task.Run(async () =>
        {
            await using var sourceStream = await photo.OpenReadAsync();
            using var original = PlatformImage.FromStream(sourceStream);

            var scale = Math.Min(1f, MaxDimension / Math.Max(original.Width, original.Height));
            var resized = scale < 1f
                ? original.Resize(original.Width * scale, original.Height * scale, ResizeMode.Fit, disposeOriginal: false)
                : original;
            try
            {
                await using var localStream = File.OpenWrite(localPath);
                resized.Save(localStream, ImageFormat.Jpeg, JpegQuality);
            }
            finally
            {
                if (!ReferenceEquals(resized, original))
                {
                    resized.Dispose();
                }
            }
        });

        return localPath;
    }
}

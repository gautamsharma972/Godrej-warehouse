namespace WarehouseGate.Mobile.Models;

// Flattened view of Photo/OutwardPhoto (identical shape, two separate DTO classes) so the
// carousel, tile template, and PhotoViewerPage are written once and shared by both job-detail pages.
public class PhotoDisplayItem
{
    public required int Id { get; init; }
    public required string Type { get; init; }
    public required DateTime CapturedAt { get; init; }
    public string? LocalPath { get; init; }

    public bool HasLocalImage => LocalPath is not null && File.Exists(LocalPath);
    public bool IsPlaceholder => !HasLocalImage;
    public ImageSource? Source => HasLocalImage ? ImageSource.FromFile(LocalPath) : null;

    public string FriendlyType => Type switch
    {
        "ExceptionProof" => "Exception",
        "LoadGroupConfirmation" => "Load confirmation",
        _ when Type.StartsWith("Vehicle", StringComparison.Ordinal) => "Vehicle",
        _ when Type.StartsWith("Material", StringComparison.Ordinal) => "Material",
        _ => Type
    };

    public string Description => $"{FriendlyType} photo · Captured {CapturedAt.ToLocalTime():d MMM, h:mm tt}";
}

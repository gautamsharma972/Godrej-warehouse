namespace WarehouseGate.Domain;

public class Vehicle : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string Number { get; set; } = string.Empty;

    // Capacity used by the suggested loading-sequence algorithm. Nullable - Vehicle rows are
    // created lazily at Dock-In/gate-in with no capacity entered, and get backfilled with the
    // defaults below whenever still missing (see AdminController.CreateVehicle/UpdateVehicle,
    // InwardService.CheckInAsync, OutwardService.BackfillVehicleCapacity).
    public decimal? MaxWeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }

    // Generic mid-size truck capacity - applied per-field, only where a value is still missing.
    public const decimal DefaultMaxWeightKg = 18000m;
    public const decimal DefaultLengthCm = 480.00m;
    public const decimal DefaultWidthCm = 200.00m;
    public const decimal DefaultHeightCm = 190.00m;
}

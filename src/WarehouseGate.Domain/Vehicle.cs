namespace WarehouseGate.Domain;

public class Vehicle : ITenantScoped
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public string Number { get; set; } = string.Empty;

    // Capacity used by the suggested loading-sequence algorithm. Nullable - Vehicle rows are
    // created lazily at Dock-In/gate-in with no capacity entered, and get backfilled from
    // VehicleMaster when a matching record exists.
    public decimal? MaxWeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
}

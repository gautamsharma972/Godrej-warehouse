namespace WarehouseGate.Domain;

public class VehicleMaster
{
    public int Id { get; set; }
    public int VehicleTypeId { get; set; }
    public VehicleType? VehicleType { get; set; }
    public int VehicleCategoryId { get; set; }
    public VehicleCategory? VehicleCategory { get; set; }

    // Reference capacity for this vehicle profile, used by the loading-sequence algorithm.
    public decimal? MaxWeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
}

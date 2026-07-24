namespace WarehouseGate.Mobile.Services;

public record VehicleMasterRecord(string? DriverName, string? DriverMobile, string? TransporterName, string? DispatchOrderNumber);

public static class VehicleLookupService
{
    public static readonly string[] KnownTransporters =
    {
        "Bharat Logistics", "Gujarat Freight Carriers", "Karnataka Roadlines",
        "Speed Cargo Movers", "National Transport Co.", "Shree Balaji Carriers"
    };

    // Best-effort convenience auto-fill, not a critical path - any failure (not found, timeout,
    // network down) is treated the same way: leave the guard's own entries untouched.
    public static async Task<VehicleMasterRecord?> LookupAsync(string vehicleNumber)
    {
        try
        {
            var dto = await ApiClient.GetVehicleMasterAsync(vehicleNumber.Trim());
            return dto is null ? null : new VehicleMasterRecord(dto.DriverName, dto.DriverMobile, dto.TransporterName, dto.DispatchOrderNumber);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Inward transaction numbers must be unique per check-in (the backend rejects reuse), so unlike
    // the rest of the vehicle-master fields this can't be a fixed per-vehicle value - it's generated
    // fresh each time, matching how a real system would assign one at check-in.
    public static string GenerateInwardTxnNumber() => $"TXN-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
}

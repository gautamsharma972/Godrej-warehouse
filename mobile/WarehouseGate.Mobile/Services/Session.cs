namespace WarehouseGate.Mobile.Services;

public static class Session
{
    public static string? Token { get; set; }
    public static string? Role { get; set; }
    public static string? DisplayName { get; set; }
    public static string? WarehouseName { get; set; }
    public static string? RegionName { get; set; }

    public static bool IsSupervisor => Role == "Supervisor";
    public static bool IsSecurity => Role == "Security";
    public static string ScopeLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(WarehouseName))
            {
                return WarehouseName;
            }

            if (!string.IsNullOrWhiteSpace(RegionName))
            {
                return RegionName;
            }

            return Role switch
            {
                "LogisticsManager" => "Mapped region",
                "Security" or "Supervisor" => "Mapped warehouse",
                _ => "Assigned scope"
            };
        }
    }

    public static void Clear()
    {
        Token = null;
        Role = null;
        DisplayName = null;
        WarehouseName = null;
        RegionName = null;
    }
}

namespace WarehouseGate.Mobile.Services;

public static class Session
{
    private const string TokenKey = "session.token";
    private const string RoleKey = "session.role";
    private const string DisplayNameKey = "session.displayName";
    private const string WarehouseNameKey = "session.warehouseName";
    private const string RegionNameKey = "session.regionName";
    private const string ExpiresAtUtcKey = "session.expiresAtUtc";

    public static string? Token { get; set; }
    public static string? Role { get; set; }
    public static string? DisplayName { get; set; }
    public static string? WarehouseName { get; set; }
    public static string? RegionName { get; set; }
    public static DateTime? ExpiresAtUtc { get; set; }

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

    // Called once at app startup (see AppShell) before the Shell shows anything - Preferences reads
    // are synchronous/local, so this needs no async ceremony. Only a stored token that hasn't yet
    // passed the same ExpiresAtUtc the login API returned counts as valid; anything else (nothing
    // stored, or the day's up) falls through to the normal login screen and wipes any stale entry.
    public static bool TryRestore()
    {
        var token = Preferences.Get(TokenKey, string.Empty);
        var expiresRaw = Preferences.Get(ExpiresAtUtcKey, string.Empty);
        if (string.IsNullOrEmpty(token) ||
            !DateTime.TryParse(expiresRaw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAtUtc) ||
            expiresAtUtc <= DateTime.UtcNow)
        {
            ClearStored();
            return false;
        }

        Token = token;
        Role = Preferences.Get(RoleKey, string.Empty);
        DisplayName = Preferences.Get(DisplayNameKey, string.Empty);
        WarehouseName = NullIfEmpty(Preferences.Get(WarehouseNameKey, string.Empty));
        RegionName = NullIfEmpty(Preferences.Get(RegionNameKey, string.Empty));
        ExpiresAtUtc = expiresAtUtc;
        return true;
    }

    // Called right after a successful login so the next app launch can skip straight back to the
    // dashboard instead of asking for credentials again.
    public static void Persist()
    {
        Preferences.Set(TokenKey, Token ?? string.Empty);
        Preferences.Set(RoleKey, Role ?? string.Empty);
        Preferences.Set(DisplayNameKey, DisplayName ?? string.Empty);
        Preferences.Set(WarehouseNameKey, WarehouseName ?? string.Empty);
        Preferences.Set(RegionNameKey, RegionName ?? string.Empty);
        Preferences.Set(ExpiresAtUtcKey, (ExpiresAtUtc ?? DateTime.UtcNow).ToString("o"));
    }

    public static void Clear()
    {
        Token = null;
        Role = null;
        DisplayName = null;
        WarehouseName = null;
        RegionName = null;
        ExpiresAtUtc = null;
        ClearStored();
    }

    private static void ClearStored()
    {
        Preferences.Remove(TokenKey);
        Preferences.Remove(RoleKey);
        Preferences.Remove(DisplayNameKey);
        Preferences.Remove(WarehouseNameKey);
        Preferences.Remove(RegionNameKey);
        Preferences.Remove(ExpiresAtUtcKey);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

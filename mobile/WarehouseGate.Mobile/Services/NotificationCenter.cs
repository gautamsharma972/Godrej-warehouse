namespace WarehouseGate.Mobile.Services;

// Minimal shared badge count - no fetching of its own. Pages that already compute a
// notification-equivalent total (SecurityDashboardPage, SupervisorHomePage, NotificationsPage
// itself) push it here right after finishing their existing load, so AppHeaderView's bell badge
// stays current without any new API calls.
public static class NotificationCenter
{
    private const string DismissedBaselineKeyPrefix = "notifications.dismissedBaseline";
    private static int _rawCount;
    private static int _dismissedBaseline;
    private static string? _loadedContextKey;

    public static int Count { get; private set; }

    public static event Action<int>? CountChanged;

    public static void SetCount(int count)
    {
        EnsureContextLoaded();
        _rawCount = Math.Max(0, count);

        // If previously dismissed alerts have since resolved, lower the baseline as well. A
        // later increase then represents genuinely new work and becomes visible in the badge.
        if (_rawCount < _dismissedBaseline)
        {
            _dismissedBaseline = _rawCount;
            SaveDismissedBaseline();
        }

        SetVisibleCount(Math.Max(0, _rawCount - _dismissedBaseline));
    }

    public static void DismissCurrent()
    {
        EnsureContextLoaded();
        _dismissedBaseline = _rawCount;
        SaveDismissedBaseline();
        SetVisibleCount(0);
    }

    public static void Reset()
    {
        EnsureContextLoaded();
        _rawCount = 0;
        _dismissedBaseline = 0;
        if (_loadedContextKey is not null)
        {
            Preferences.Remove(_loadedContextKey);
        }
        SetVisibleCount(0);
    }

    private static void EnsureContextLoaded()
    {
        var identity = string.Join("|", Session.Role ?? string.Empty, Session.DisplayName ?? string.Empty,
            Session.WarehouseName ?? string.Empty, Session.RegionName ?? string.Empty);
        var contextKey = $"{DismissedBaselineKeyPrefix}.{Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(identity))}";
        if (string.Equals(_loadedContextKey, contextKey, StringComparison.Ordinal))
        {
            return;
        }

        _loadedContextKey = contextKey;
        _rawCount = 0;
        _dismissedBaseline = Math.Max(0, Preferences.Get(contextKey, 0));
        Count = 0;
    }

    private static void SaveDismissedBaseline()
    {
        if (_loadedContextKey is not null)
        {
            Preferences.Set(_loadedContextKey, _dismissedBaseline);
        }
    }

    private static void SetVisibleCount(int count)
    {
        if (Count == count)
        {
            return;
        }

        Count = count;
        CountChanged?.Invoke(count);
    }
}

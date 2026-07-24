namespace WarehouseGate.Mobile.Services;

// Minimal shared badge count - no fetching of its own. Pages that already compute a
// notification-equivalent total (SecurityDashboardPage, SupervisorHomePage, NotificationsPage
// itself) push it here right after finishing their existing load, so AppHeaderView's bell badge
// stays current without any new API calls.
public static class NotificationCenter
{
    public static int Count { get; private set; }

    public static event Action<int>? CountChanged;

    public static void SetCount(int count)
    {
        if (Count == count)
        {
            return;
        }

        Count = count;
        CountChanged?.Invoke(count);
    }
}

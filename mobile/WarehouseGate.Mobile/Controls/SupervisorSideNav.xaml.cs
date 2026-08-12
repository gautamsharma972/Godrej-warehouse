using WarehouseGate.Mobile.Pages;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public partial class SupervisorSideNav : ContentView
{
    private const double ExpandedFlyoutWidth = 252;

    public SupervisorSideNav()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            Shell.Current.Navigated += OnShellNavigated;
        }

        NotificationCenter.CountChanged += OnNotificationCountChanged;
        OnNotificationCountChanged(NotificationCenter.Count);
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutWidth = ExpandedFlyoutWidth;
        }
        UpdateActiveState();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            Shell.Current.Navigated -= OnShellNavigated;
        }

        NotificationCenter.CountChanged -= OnNotificationCountChanged;
    }

    // Custom flyout content (not native FlyoutItem/MenuItem elements) doesn't auto-dismiss the
    // drawer on navigation the way Shell's own flyout items would - close it manually here so
    // tapping any nav row on a phone-width drawer behaves like every other hamburger menu.
    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        UpdateActiveState();
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = false;
        }
    }

    private void OnNotificationCountChanged(int count) => MainThread.BeginInvokeOnMainThread(() =>
    {
        AlertsBadge.IsVisible = count > 0;
        AlertsBadgeLabel.Text = count > 9 ? "9+" : count.ToString();
    });

    private void UpdateActiveState()
    {
        var location = Shell.Current?.CurrentState?.Location?.ToString() ?? string.Empty;
        var currentPage = Shell.Current?.CurrentPage;
        var activeItem = GetActiveItem(location, currentPage);

        SetItemState(HomePill, HomeIcon, HomeLabel, activeItem == "Home");
        SetItemState(JobsPill, JobsIcon, JobsLabel, activeItem == "Jobs");
        SetItemState(HistoryPill, HistoryIcon, HistoryLabel, activeItem == "History");
        SetItemState(AlertsPill, AlertsIcon, AlertsLabel, activeItem == "Alerts");
        SetItemState(SettingsPill, SettingsIcon, SettingsLabel, activeItem == "Settings");
    }

    private static string GetActiveItem(string location, Page? currentPage)
    {
        if (currentPage is NotificationsPage)
        {
            return "Alerts";
        }

        if (currentPage is AccountPage)
        {
            return "Settings";
        }

        if (currentPage is SupervisorHistoryPage)
        {
            return "History";
        }

        if (currentPage is SupervisorHomePage or JobDetailPage or OutwardJobDetailPage or LoadPlanEditorPage or LoadConfirmationPage)
        {
            return "Jobs";
        }

        if (currentPage is SupervisorDashboardPage)
        {
            return "Home";
        }

        if (location.Contains("NotificationsPage"))
        {
            return "Alerts";
        }

        if (location.Contains("AccountPage"))
        {
            return "Settings";
        }

        if (location.Contains("SupervisorHistoryPage"))
        {
            return "History";
        }

        if (location.Contains("SupervisorHomePage") || location.Contains("JobDetailPage") ||
            location.Contains("OutwardJobDetailPage") || location.Contains("LoadPlanEditorPage") ||
            location.Contains("LoadConfirmationPage"))
        {
            return "Jobs";
        }

        return location.Contains("SupervisorDashboardPage") ? "Home" : string.Empty;
    }

    private static void SetItemState(Border pill, Label icon, Label label, bool active)
    {
        var activeColor = (Color)Application.Current!.Resources["Primary"];
        var inactiveColor = (Color)Application.Current.Resources["TextSecondaryLight"];
        pill.BackgroundColor = active ? Color.FromArgb("#EAF1FF") : Colors.Transparent;
        icon.TextColor = active ? activeColor : inactiveColor;
        label.TextColor = active ? activeColor : (Color)Application.Current.Resources["TextPrimaryLight"];
        label.FontFamily = active ? "PoppinsSemiBold" : "PoppinsRegular";
    }

    private async void OnHomeTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorDashboardPage");

    private async void OnJobsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHomePage");

    private async void OnHistoryTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHistoryPage");

    private async void OnAlertsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));

    private async void OnSettingsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(AccountPage));

    private async void OnLogoutTapped(object? sender, EventArgs e)
    {
        var shell = Shell.Current;
        var page = shell?.CurrentPage;
        if (page is null)
        {
            return;
        }

        var confirmed = await page.DisplayAlert("Log out?", "You'll need to sign in again to continue.", "Log out", "Cancel");
        if (!confirmed)
        {
            return;
        }

        await SupervisorHubClient.StopAsync();
        Session.Clear();
        await shell!.GoToAsync("//LoginPage");
    }
}

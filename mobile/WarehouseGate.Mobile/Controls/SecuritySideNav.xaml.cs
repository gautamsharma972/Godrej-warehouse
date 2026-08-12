using WarehouseGate.Mobile.Services;
using WarehouseGate.Mobile.Pages;

namespace WarehouseGate.Mobile.Controls;

// The security section's shared left sidebar, hosted as the Shell's flyout content and
// locked open on every security page - one definition, present everywhere. Tracks the
// active page from Shell navigation and mirrors the bell badge via NotificationCenter.
public partial class SecuritySideNav : ContentView
{
    private const double ExpandedFlyoutWidth = 252;

    public SecuritySideNav()
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
        SetItemState(CheckInPill, CheckInIcon, CheckInLabel, activeItem == "CheckIn");
        SetItemState(StatusPill, StatusIcon, StatusLabel, activeItem == "Status");
        SetItemState(OutwardsPill, OutwardsIcon, OutwardsLabel, activeItem == "Outwards");
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

        if (currentPage is VehicleExitPage)
        {
            return "Outwards";
        }

        if (currentPage is SecurityStatusPage or JobDetailPage or OutwardJobDetailPage)
        {
            return "Status";
        }

        if (currentPage is SecurityHomePage)
        {
            return "CheckIn";
        }

        if (currentPage is SecurityDashboardPage)
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

        if (location.Contains("VehicleExitPage"))
        {
            return "Outwards";
        }

        if (location.Contains("SecurityStatusPage") || location.Contains("JobDetailPage") || location.Contains("OutwardJobDetailPage"))
        {
            return "Status";
        }

        if (location.Contains("SecurityHomePage"))
        {
            return "CheckIn";
        }

        return location.Contains("SecurityDashboardPage") ? "Home" : string.Empty;
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
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityDashboardPage");

    private async void OnCheckInTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityHomePage");

    private async void OnStatusTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(SecurityStatusPage));

    private async void OnOutwardsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityExitPage");

    private async void OnAlertsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("NotificationsPage");

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

        Session.Clear();
        await shell!.GoToAsync("//LoginPage");
    }
}

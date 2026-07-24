using WarehouseGate.Mobile.Services;
using WarehouseGate.Mobile.Pages;

namespace WarehouseGate.Mobile.Controls;

// The security section's shared left sidebar, hosted as the Shell's flyout content and
// locked open on every security page - one definition, present everywhere. Tracks the
// active page from Shell navigation and mirrors the bell badge via NotificationCenter.
public partial class SecuritySideNav : ContentView
{
    private const double ExpandedFlyoutWidth = 252;
    private const double CollapsedFlyoutWidth = 96;
    private bool _isCollapsed;

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
        ApplyCollapsedState();
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

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e) => UpdateActiveState();

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

        if (location.Contains("SecurityAccountPage"))
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
        pill.BackgroundColor = active ? Color.FromArgb("#E9FBF8") : Colors.Transparent;
        icon.TextColor = active ? activeColor : inactiveColor;
        label.TextColor = active ? activeColor : (Color)Application.Current.Resources["TextPrimaryLight"];
        label.FontFamily = active ? "PoppinsSemiBold" : "PoppinsRegular";
    }

    private void OnToggleCollapseTapped(object? sender, EventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        ApplyCollapsedState();
    }

    private void ApplyCollapsedState()
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutWidth = _isCollapsed ? CollapsedFlyoutWidth : ExpandedFlyoutWidth;
        }

        SidebarRoot.Padding = _isCollapsed
            ? new Thickness(14, 24)
            : new Thickness(20, 24);
        NavStack.Spacing = _isCollapsed ? 8 : 6;
        CollapseIcon.Text = _isCollapsed ? IconGlyphs.ChevronRight : IconGlyphs.ChevronLeft;
        BrandCopy.IsVisible = !_isCollapsed;
        LogoutLabel.IsVisible = !_isCollapsed;
        LogoutPill.Padding = _isCollapsed ? new Thickness(0, 13) : new Thickness(14, 13);

        SetLabelVisibility(!_isCollapsed);
        SetNavPadding(_isCollapsed ? new Thickness(0, 13) : new Thickness(14, 13));
        SetNavAlignment(_isCollapsed);
    }

    private void SetLabelVisibility(bool visible)
    {
        HomeLabel.IsVisible = visible;
        CheckInLabel.IsVisible = visible;
        StatusLabel.IsVisible = visible;
        OutwardsLabel.IsVisible = visible;
        AlertsLabel.IsVisible = visible;
        SettingsLabel.IsVisible = visible;
    }

    private void SetNavPadding(Thickness padding)
    {
        HomePill.Padding = padding;
        CheckInPill.Padding = padding;
        StatusPill.Padding = padding;
        OutwardsPill.Padding = padding;
        AlertsPill.Padding = padding;
        SettingsPill.Padding = padding;
    }

    private void SetNavAlignment(bool collapsed)
    {
        var rowAlignment = collapsed ? LayoutOptions.Center : LayoutOptions.Start;
        HomeRow.HorizontalOptions = rowAlignment;
        CheckInRow.HorizontalOptions = rowAlignment;
        StatusRow.HorizontalOptions = rowAlignment;
        OutwardsRow.HorizontalOptions = rowAlignment;
        AlertsRow.HorizontalOptions = rowAlignment;
        SettingsRow.HorizontalOptions = rowAlignment;
        LogoutRow.HorizontalOptions = LayoutOptions.Center;
    }

    private async void OnHomeTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityDashboardPage");

    private async void OnCheckInTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityHomePage");

    private async void OnStatusTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityStatusPage");

    private async void OnOutwardsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/VehicleExitPage");

    private async void OnAlertsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("NotificationsPage");

    private async void OnSettingsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityAccountPage");

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

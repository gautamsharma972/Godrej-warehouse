namespace WarehouseGate.Mobile.Controls;

public partial class SecurityBottomNavBar : ContentView
{
    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(SecurityBottomNavBar), string.Empty,
        propertyChanged: (bindable, _, _) => ((SecurityBottomNavBar)bindable).UpdateActiveState());

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    public SecurityBottomNavBar()
    {
        InitializeComponent();
        UpdateActiveState();
    }

    private void UpdateActiveState()
    {
        SetItemState(DashboardIcon, DashboardLabel, DashboardPill, ActiveTab == "Dashboard");
        SetItemState(CheckInIcon, CheckInLabel, CheckInPill, ActiveTab == "CheckIn");
        SetItemState(StatusIcon, StatusLabel, StatusPill, ActiveTab == "Status");
        SetItemState(ExitIcon, ExitLabel, ExitPill, ActiveTab == "Exit");
    }

    private static void SetItemState(Label icon, Label label, Border pill, bool active)
    {
        var activeColor = (Color)Application.Current!.Resources["Primary"];
        var inactiveColor = (Color)Application.Current!.Resources["Gray400"];
        icon.TextColor = active ? activeColor : inactiveColor;
        label.TextColor = active ? activeColor : inactiveColor;
        pill.IsVisible = active;
    }

    private async void OnDashboardTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityDashboardPage");

    private async void OnCheckInTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityHomePage");

    private async void OnStatusTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityStatusPage");

    private async void OnExitTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/VehicleExitPage");
}

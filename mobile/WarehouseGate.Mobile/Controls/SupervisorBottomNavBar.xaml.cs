namespace WarehouseGate.Mobile.Controls;

public partial class SupervisorBottomNavBar : ContentView
{
    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(SupervisorBottomNavBar), string.Empty,
        propertyChanged: (bindable, _, _) => ((SupervisorBottomNavBar)bindable).UpdateActiveState());

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    public SupervisorBottomNavBar()
    {
        InitializeComponent();
        UpdateActiveState();
    }

    private void UpdateActiveState()
    {
        SetItemState(DashboardIcon, DashboardLabel, DashboardPill, ActiveTab == "Dashboard");
        SetItemState(JobsIcon, JobsLabel, JobsPill, ActiveTab == "Jobs");
        SetItemState(HistoryIcon, HistoryLabel, HistoryPill, ActiveTab == "History");
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
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorDashboardPage");

    private async void OnJobsTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHomePage");

    private async void OnHistoryTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHistoryPage");
}

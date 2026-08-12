using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class AccountPage : ContentPage
{
    public AccountPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        NameLabel.Text = Session.DisplayName;
        RoleLabel.Text = Session.Role?.ToUpperInvariant();
        RoleDetailLabel.Text = Session.Role;
        ScopeLabel.Text = Session.ScopeLabel;
        VersionLabel.Text = $"{AppInfo.Current.VersionString} ({AppInfo.Current.BuildString})";
        PageHeader.BackRoute = Session.IsSupervisor
            ? "//SupervisorTabs/SupervisorDashboardPage"
            : "//SecurityTabs/SecurityDashboardPage";
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert("Log out?", "You'll need to sign in again to continue.", "Log out", "Cancel");
        if (!confirmed)
        {
            return;
        }

        if (Session.IsSupervisor)
        {
            await SupervisorHubClient.StopAsync();
        }

        Session.Clear();
        await Shell.Current.GoToAsync("//LoginPage");
    }
}

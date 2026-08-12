using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class MoreMenuPage : ContentPage
{
    public MoreMenuPage()
    {
        InitializeComponent();
        StatusMenuRow.IsVisible = Session.IsSecurity;
    }

    private async void OnBackdropTapped(object? sender, TappedEventArgs e) =>
        await Navigation.PopModalAsync(false);

    private async Task CloseAndNavigateAsync(string route)
    {
        await Navigation.PopModalAsync(false);
        await Shell.Current.GoToAsync(route);
    }

    private async void OnStatusTapped(object? sender, TappedEventArgs e) =>
        await CloseAndNavigateAsync(nameof(SecurityStatusPage));

    private async void OnNotificationsTapped(object? sender, TappedEventArgs e) =>
        await CloseAndNavigateAsync(nameof(NotificationsPage));

    private async void OnProfileTapped(object? sender, TappedEventArgs e) =>
        await CloseAndNavigateAsync(nameof(AccountPage));

    private async void OnLogoutTapped(object? sender, TappedEventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Log out?",
            "You'll need to sign in again to continue.",
            "Log out",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        var stopSupervisorHub = Session.IsSupervisor;
        await Navigation.PopModalAsync(false);
        if (stopSupervisorHub)
        {
            await SupervisorHubClient.StopAsync();
        }

        Session.Clear();
        await Shell.Current.GoToAsync("//LoginPage");
    }
}

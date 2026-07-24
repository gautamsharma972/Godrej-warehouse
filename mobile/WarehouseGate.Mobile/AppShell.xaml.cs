using WarehouseGate.Mobile.Pages;

namespace WarehouseGate.Mobile;

public partial class AppShell : Shell
{
    private string? _lastAnimatedLocation;

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(JobDetailPage), typeof(JobDetailPage));
        Routing.RegisterRoute(nameof(OutwardJobDetailPage), typeof(OutwardJobDetailPage));
        Routing.RegisterRoute(nameof(NotificationsPage), typeof(NotificationsPage));
        Routing.RegisterRoute(nameof(LoadPlanEditorPage), typeof(LoadPlanEditorPage));
        Routing.RegisterRoute(nameof(LoadConfirmationPage), typeof(LoadConfirmationPage));
        Navigated += OnShellNavigated;
    }

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        var location = CurrentState?.Location?.ToString();
        var page = CurrentPage;
        if (page is null || string.IsNullOrWhiteSpace(location) || location == _lastAnimatedLocation)
        {
            return;
        }

        _lastAnimatedLocation = location;
        await AnimatePageFromRightAsync(page);
    }

    private static async Task AnimatePageFromRightAsync(Page page)
    {
        var width = page.Width > 0
            ? page.Width
            : DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;

        page.AbortAnimation("PageSlideIn");
        page.TranslationX = Math.Min(width, 520);
        page.Opacity = 0.96;

        await Task.WhenAll(
            page.TranslateTo(0, 0, 220, Easing.CubicOut),
            page.FadeTo(1, 160, Easing.CubicOut));
    }
}

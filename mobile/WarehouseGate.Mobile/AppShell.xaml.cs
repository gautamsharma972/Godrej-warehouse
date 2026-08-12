using WarehouseGate.Mobile.Pages;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile;

public partial class AppShell : Shell
{
    private string? _lastAnimatedLocation;
    private string _lastPrimaryRoute = "//SecurityTabs/SecurityDashboardPage";
    private bool _isOpeningMoreMenu;

    public async Task<bool> HandleHardwareBackAsync()
    {
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync();
            return true;
        }

        var location = CurrentState?.Location?.ToString() ?? string.Empty;
        if (Navigation.NavigationStack.Count > 1 || IsChildRoute(location))
        {
            await GoToAsync("..");
            return true;
        }

        var dashboardRoute = Session.IsSupervisor
            ? "//SupervisorTabs/SupervisorDashboardPage"
            : "//SecurityTabs/SecurityDashboardPage";
        if (!location.EndsWith(dashboardRoute.TrimStart('/'), StringComparison.OrdinalIgnoreCase)
            && !location.EndsWith("LoginPage", StringComparison.OrdinalIgnoreCase))
        {
            await GoToAsync(dashboardRoute);
            return true;
        }

        return false;
    }

    private static bool IsChildRoute(string location) =>
        new[]
        {
            nameof(JobDetailPage), nameof(OutwardJobDetailPage), nameof(NotificationsPage),
            nameof(SecurityStatusPage), nameof(AccountPage), nameof(LoadPlanEditorPage),
            nameof(LoadSimulationPage), nameof(LoadConfirmationPage)
        }.Any(route => location.Contains(route, StringComparison.OrdinalIgnoreCase));

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(nameof(JobDetailPage), typeof(JobDetailPage));
        Routing.RegisterRoute(nameof(OutwardJobDetailPage), typeof(OutwardJobDetailPage));
        Routing.RegisterRoute(nameof(NotificationsPage), typeof(NotificationsPage));
        Routing.RegisterRoute(nameof(VehicleExitPage), typeof(VehicleExitPage));
        Routing.RegisterRoute(nameof(SecurityStatusPage), typeof(SecurityStatusPage));
        Routing.RegisterRoute(nameof(AccountPage), typeof(AccountPage));
        Routing.RegisterRoute(nameof(LoadPlanEditorPage), typeof(LoadPlanEditorPage));
        Routing.RegisterRoute(nameof(LoadSimulationPage), typeof(LoadSimulationPage));
        Routing.RegisterRoute(nameof(LoadConfirmationPage), typeof(LoadConfirmationPage));
        Navigating += OnShellNavigating;
        Navigated += OnShellNavigated;
        Loaded += OnShellLoaded;
    }

    private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        var target = e.Target.Location.OriginalString;
        if (!target.Contains("SecurityMorePage", StringComparison.OrdinalIgnoreCase)
            && !target.Contains("SupervisorMorePage", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel();
        if (_isOpeningMoreMenu)
        {
            return;
        }

        _isOpeningMoreMenu = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                // MAUI may update the selected ShellContent before a tab navigation is cancelled.
                // Put Shell back on the last real tab first so pages opened from More are never
                // pushed onto the hidden More tab's navigation stack.
                await GoToAsync(_lastPrimaryRoute, false);
                await Navigation.PushModalAsync(new MoreMenuPage(), false);
            }
            finally
            {
                _isOpeningMoreMenu = false;
            }
        });
    }

    // LoginPage is always the first ShellContent (Shell needs SOME initial route), so a returning
    // user briefly sees it before this fires - Loaded is the earliest point the Shell is actually
    // attached and GoToAsync is safe to call. A stored, not-yet-expired session (see
    // Session.TryRestore) skips straight past it to the right dashboard, same as a fresh login does.
    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;

        if (!Session.TryRestore())
        {
            return;
        }

        _ = SupervisorHubClient.StartAsync();
        await GoToAsync(Session.IsSupervisor
            ? "//SupervisorTabs/SupervisorDashboardPage"
            : "//SecurityTabs/SecurityDashboardPage");
    }

    private async void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
#if ANDROID
        ApplyAndroidBottomNavigationStyle();
#endif
        var location = CurrentState?.Location?.ToString();
        var page = CurrentPage;
        if (page is null || string.IsNullOrWhiteSpace(location) || location == _lastAnimatedLocation)
        {
            return;
        }

        _lastAnimatedLocation = location;

        if (TryGetPrimaryRoute(location, out var primaryRoute))
        {
            _lastPrimaryRoute = primaryRoute;
        }

        await AnimatePageFromRightAsync(page);
    }

    private static bool TryGetPrimaryRoute(string location, out string route)
    {
        var primaryRoutes = new[]
        {
            "//SecurityTabs/SecurityDashboardPage",
            "//SecurityTabs/SecurityHomePage",
            "//SecurityTabs/SecurityExitPage",
            "//SupervisorTabs/SupervisorDashboardPage",
            "//SupervisorTabs/SupervisorHomePage",
            "//SupervisorTabs/SupervisorHistoryPage"
        };

        route = primaryRoutes.FirstOrDefault(candidate =>
            location.EndsWith(candidate.TrimStart('/'), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return route.Length > 0;
    }

#if ANDROID
    private static void ApplyAndroidBottomNavigationStyle()
    {
        var decorView = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;
        if (decorView is null)
        {
            return;
        }

        decorView.Post(() =>
        {
            var bottomNavigation = FindBottomNavigation(decorView);
            if (bottomNavigation is null)
            {
                return;
            }

            var colors = new[]
            {
                Android.Graphics.Color.ParseColor("#FFFFFF").ToArgb(),
                Android.Graphics.Color.ParseColor("#F2F7FC").ToArgb(),
                Android.Graphics.Color.ParseColor("#E8F2FB").ToArgb()
            };
            var gradient = new Android.Graphics.Drawables.GradientDrawable(
                Android.Graphics.Drawables.GradientDrawable.Orientation.LeftRight,
                colors);
            var density = bottomNavigation.Resources?.DisplayMetrics?.Density ?? 1f;
            gradient.SetStroke(Math.Max(1, (int)density), Android.Graphics.Color.ParseColor("#D7E4F2"));
            bottomNavigation.BackgroundTintList = null;
            bottomNavigation.Background = gradient;
            bottomNavigation.Elevation = 8 * density;
            var basePadding = (int)(3 * density);
            var insets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(bottomNavigation);
            var navigationBottom = insets?
                .GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.NavigationBars()).Bottom ?? 0;
            bottomNavigation.SetPadding(0, basePadding, 0, basePadding + navigationBottom);
        });
    }

    private static Google.Android.Material.BottomNavigation.BottomNavigationView? FindBottomNavigation(Android.Views.View view)
    {
        if (view is Google.Android.Material.BottomNavigation.BottomNavigationView bottomNavigation)
        {
            return bottomNavigation;
        }

        if (view is not Android.Views.ViewGroup group)
        {
            return null;
        }

        for (var index = 0; index < group.ChildCount; index++)
        {
            var child = group.GetChildAt(index);
            if (child is null)
            {
                continue;
            }

            var match = FindBottomNavigation(child);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
#endif

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

using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SecurityDashboardPage : ContentPage
{
    // A vehicle still sitting at the gate past this many minutes likely means no supervisor has
    // noticed/claimed it yet - surfaced here so Security doesn't have to walk over and ask.
    private const int GateWaitAlertThresholdMinutes = 30;
    private bool _isLoading;
    private bool? _isWideLayout;

    public SecurityDashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScopeLabel.Text = Session.ScopeLabel;

        SupervisorHubClient.JobAvailable += OnHubJobChanged;
        SupervisorHubClient.JobClaimed += OnHubJobChanged;
        SupervisorHubClient.JobUpdated += OnHubJobChanged;

        _ = LoadAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = ResponsiveHelper.IsWide(width);
        if (_isWideLayout == wide)
        {
            return;
        }

        _isWideLayout = wide;
        PageContent.Padding = wide ? new Thickness(30, 26, 30, 30) : new Thickness(16, 18, 16, 24);
        ResponsiveHelper.ConfigureStackableGrid(StatsGrid, wide, wideColumnCount: 2);
        ResponsiveHelper.ConfigureStackableGrid(QuickActionsGrid, wide, wideColumnCount: 3);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobAvailable -= OnHubJobChanged;
        SupervisorHubClient.JobClaimed -= OnHubJobChanged;
        SupervisorHubClient.JobUpdated -= OnHubJobChanged;
    }

    private void OnHubJobChanged(InwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = LoadAsync();
            _ = ShowLiveBannerAsync($"Update: {job.VehicleNumber} — {job.Status}");
        });
    }

    private async Task ShowLiveBannerAsync(string message)
    {
        LiveBannerLabel.Text = message;
        LiveBanner.IsVisible = true;
        await LiveBanner.FadeTo(1, 150);
        await Task.Delay(2200);
        await LiveBanner.FadeTo(0, 300);
        LiveBanner.IsVisible = false;
    }

    private async Task LoadAsync()
    {
        if (_isLoading)
        {
            RefreshViewControl.IsRefreshing = false;
            return;
        }

        _isLoading = true;
        try
        {
            var activeTask = ApiClient.GetSecurityTransactionsAsync(activeOnly: true);
            var pendingExitTask = ApiClient.GetPendingExitJobsAsync();
            var completedTodayTask = ApiClient.GetSecurityTransactionsAsync(activeOnly: false, date: DateTime.Today);
            await Task.WhenAll(activeTask, pendingExitTask, completedTodayTask);

            var active = activeTask.Result;
            var pendingExit = pendingExitTask.Result;
            var completedToday = completedTodayTask.Result;

            ActiveCountLabel.Text = active.Count.ToString();
            ReadyToExitCountLabel.Text = pendingExit.Count.ToString();

            var waitingTooLong = active.Count(j =>
                j.Status == "GateIn" && (DateTime.UtcNow - j.GateInTime).TotalMinutes > GateWaitAlertThresholdMinutes);
            var readyToExit = pendingExit.Count;
            var newVehicles = active.Count(j => j.IsNewVehicle);
            var deliveryMismatches = active.Count(j => j.HasDeliveryDateMismatch);
            var exceptionsToday = completedToday.Count(j => j.Grn?.HasExceptions == true);

            RenderAttentionCards(waitingTooLong, readyToExit, newVehicles, deliveryMismatches, exceptionsToday);
            NotificationCenter.SetCount(waitingTooLong + readyToExit + newVehicles + deliveryMismatches + exceptionsToday);
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        catch (Exception)
        {
            await DisplayAlert("Error", "Could not reach the server.", "OK");
        }
        finally
        {
            _isLoading = false;
            RefreshViewControl.IsRefreshing = false;
        }
    }

    private void RenderAttentionCards(int waitingTooLong, int readyToExit, int newVehicles, int deliveryMismatches, int exceptionsToday)
    {
        AttentionContainer.Children.Clear();

        void AddAttentionRow(string icon, string colorKey, int count, string label, string route)
        {
            if (AttentionContainer.Children.Count > 0)
            {
                AttentionContainer.Children.Add(UiHelpers.BuildDashboardAttentionDivider());
            }

            AttentionContainer.Children.Add(UiHelpers.BuildDashboardAttentionRow(icon, colorKey, count, label, route));
        }

        if (waitingTooLong > 0)
        {
            AddAttentionRow(IconGlyphs.Clock, "StatusException", waitingTooLong,
                waitingTooLong == 1 ? "vehicle waiting over 30 min at the gate" : "vehicles waiting over 30 min at the gate",
                "//SecurityTabs/SecurityStatusPage");
        }

        if (readyToExit > 0)
        {
            AddAttentionRow(IconGlyphs.RightFromBracket, "StatusSuccess", readyToExit,
                readyToExit == 1 ? "vehicle ready to exit" : "vehicles ready to exit",
                "//SecurityTabs/VehicleExitPage");
        }

        if (newVehicles > 0)
        {
            AddAttentionRow(IconGlyphs.TriangleExclamation, "StatusAssigned", newVehicles,
                newVehicles == 1 ? "new vehicle flagged for office review" : "new vehicles flagged for office review",
                "//SecurityTabs/SecurityStatusPage");
        }

        if (deliveryMismatches > 0)
        {
            AddAttentionRow(IconGlyphs.CalendarDays, "StatusException", deliveryMismatches,
                deliveryMismatches == 1 ? "vehicle outside expected delivery window" : "vehicles outside expected delivery window",
                "//SecurityTabs/SecurityStatusPage");
        }

        if (exceptionsToday > 0)
        {
            AddAttentionRow(IconGlyphs.TriangleExclamation, "StatusException", exceptionsToday,
                exceptionsToday == 1 ? "vehicle completed with exceptions today — needs follow-up" : "vehicles completed with exceptions today — needs follow-up",
                "//SecurityTabs/SecurityStatusPage");
        }

        if (AttentionContainer.Children.Count == 0)
        {
            AttentionContainer.Children.Add(UiHelpers.BuildAllClearCard());
        }
    }

    private void OnRefreshing(object? sender, EventArgs e) => _ = LoadAsync();

    private async void OnCheckInQuickActionClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityHomePage");

    private async void OnStatusQuickActionClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityStatusPage");

    private async void OnExitQuickActionClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SecurityTabs/VehicleExitPage");

    private async void OnViewAllAttentionTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("NotificationsPage");
}

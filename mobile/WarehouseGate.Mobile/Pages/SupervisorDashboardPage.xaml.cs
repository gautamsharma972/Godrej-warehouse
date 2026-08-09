using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SupervisorDashboardPage : ContentPage
{
    private bool _isLoading;
    private bool? _isWideLayout;

    public SupervisorDashboardPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScopeLabel.Text = Session.ScopeLabel;

        SupervisorHubClient.JobUpdated += OnHubJobChanged;
        SupervisorHubClient.JobAssignedToYou += OnHubJobChanged;
        SupervisorHubClient.OutwardJobUpdated += OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobAssignedToYou += OnHubOutwardJobChanged;

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
        ResponsiveHelper.ConfigureStackableGrid(QuickActionsGrid, wide, wideColumnCount: 2);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobUpdated -= OnHubJobChanged;
        SupervisorHubClient.JobAssignedToYou -= OnHubJobChanged;
        SupervisorHubClient.OutwardJobUpdated -= OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobAssignedToYou -= OnHubOutwardJobChanged;
    }

    private void OnHubJobChanged(InwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = LoadAsync();
            _ = ShowLiveBannerAsync($"Update: {job.VehicleNumber} — {job.Status}");
        });
    }

    private void OnHubOutwardJobChanged(OutwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = LoadAsync();
            _ = ShowLiveBannerAsync($"Update: {job.DispatchOrderNumber} — {job.Status}");
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
            var mineTask = ApiClient.GetMyJobsAsync();
            var mineOutwardTask = ApiClient.GetMyOutwardJobsAsync();
            var historyTodayTask = ApiClient.GetInwardHistoryAsync(date: DateTime.Today);
            var historyOutwardTodayTask = ApiClient.GetOutwardHistoryAsync(date: DateTime.Today);
            await Task.WhenAll(mineTask, mineOutwardTask, historyTodayTask, historyOutwardTodayTask);

            var mine = mineTask.Result;
            var mineOutward = mineOutwardTask.Result;
            var completedToday = historyTodayTask.Result.Count + historyOutwardTodayTask.Result.Count;

            ActiveCountLabel.Text = (mine.Count + mineOutward.Count).ToString();
            CompletedTodayCountLabel.Text = completedToday.ToString();

            // Assigned by Office but not yet started (Dock-In) - the "needs your attention" metric
            // now that supervisors no longer self-claim from an unassigned pool.
            var assignedNotStarted = mine.Count(j => j.Status == "Assigned") + mineOutward.Count(j => j.Status == "Assigned");
            RenderAttentionCards(assignedNotStarted);
            NotificationCenter.SetCount(assignedNotStarted);
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

    private void RenderAttentionCards(int assignedNotStarted)
    {
        AttentionContainer.Children.Clear();

        if (assignedNotStarted > 0)
        {
            AttentionContainer.Children.Add(UiHelpers.BuildDashboardAttentionRow(
                IconGlyphs.ClipboardCheck, "StatusAssigned", assignedNotStarted,
                assignedNotStarted == 1 ? "job assigned, not yet started" : "jobs assigned, not yet started",
                "//SupervisorTabs/SupervisorHomePage"));
        }

        if (AttentionContainer.Children.Count == 0)
        {
            AttentionContainer.Children.Add(UiHelpers.BuildAllClearCard("All caught up", "No jobs need your attention right now."));
        }
    }

    private void OnRefreshing(object? sender, EventArgs e) => _ = LoadAsync();

    private async void OnJobsQuickActionClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHomePage");

    private async void OnHistoryQuickActionClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHistoryPage");

    private async void OnViewAllAttentionTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));
}

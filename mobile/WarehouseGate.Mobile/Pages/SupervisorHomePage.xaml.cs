using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SupervisorHomePage : ContentPage
{
    private bool _showingOutward;
    private int _inwardJobCount;
    private int _outwardJobCount;
    private bool _isLoading;
    private bool? _isWideLayout;

    public SupervisorHomePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        SupervisorHubClient.JobUpdated += OnHubJobChanged;
        SupervisorHubClient.JobAssignedToYou += OnHubJobAssignedToYou;

        SupervisorHubClient.OutwardJobUpdated += OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobAssignedToYou += OnHubOutwardJobAssignedToYou;

        UpdateTabStyles();
        _ = LoadJobsAsync();
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
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobUpdated -= OnHubJobChanged;
        SupervisorHubClient.JobAssignedToYou -= OnHubJobAssignedToYou;

        SupervisorHubClient.OutwardJobUpdated -= OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobAssignedToYou -= OnHubOutwardJobAssignedToYou;
    }

    private void OnHubJobChanged(InwardJob job) =>
        MainThread.BeginInvokeOnMainThread(() => _ = LoadJobsAsync());

    private void OnHubJobAssignedToYou(InwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = LoadJobsAsync();
            _ = ShowLiveBannerAsync($"You've been assigned: {job.VehicleNumber}");
        });
    }

    private void OnHubOutwardJobChanged(OutwardJob job) =>
        MainThread.BeginInvokeOnMainThread(() => _ = LoadJobsAsync());

    private void OnHubOutwardJobAssignedToYou(OutwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = LoadJobsAsync();
            _ = ShowLiveBannerAsync($"You've been assigned: {job.DispatchOrderNumber}");
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

    private async Task LoadJobsAsync()
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
            await Task.WhenAll(mineTask, mineOutwardTask);

            var mine = mineTask.Result;
            var mineOutward = mineOutwardTask.Result;

            MyJobsCollectionView.ItemsSource = mine;
            NoMineLabel.IsVisible = mine.Count == 0;
            InwardMineCountLabel.Text = mine.Count.ToString();

            MyOutwardJobsCollectionView.ItemsSource = mineOutward;
            NoMineOutwardLabel.IsVisible = mineOutward.Count == 0;
            OutwardMineCountLabel.Text = mineOutward.Count.ToString();

            _inwardJobCount = mine.Count;
            _outwardJobCount = mineOutward.Count;
            InwardRadioLabel.Text = $"Inward ({_inwardJobCount})";
            OutwardRadioLabel.Text = $"Outward ({_outwardJobCount})";
            UpdateHeaderCopy();

            // Jobs Office assigned to you that you haven't started yet - the notification bell
            // badge now tracks "needs your attention" instead of the old self-claim queue.
            var assignedNotStarted = mine.Count(j => j.Status == "Assigned") + mineOutward.Count(j => j.Status == "Assigned");
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

    private void OnRefreshing(object? sender, EventArgs e) => _ = LoadJobsAsync();

    private void OnInwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutward = false;
        UpdateTabStyles();
    }

    private void OnOutwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutward = true;
        UpdateTabStyles();
    }

    private void UpdateTabStyles()
    {
        InwardSection.IsVisible = !_showingOutward;
        OutwardSection.IsVisible = _showingOutward;
        UpdateHeaderCopy();

        var selectedText = (Color)Application.Current!.Resources["Primary"];
        var unselectedText = (Color)Application.Current.Resources["TextSecondaryLight"];

        InwardTabPill.BackgroundColor = _showingOutward ? Colors.Transparent : Color.FromArgb("#E9FBF8");
        OutwardTabPill.BackgroundColor = _showingOutward ? Color.FromArgb("#E9FBF8") : Colors.Transparent;
        InwardRadioLabel.TextColor = _showingOutward ? unselectedText : selectedText;
        OutwardRadioLabel.TextColor = _showingOutward ? selectedText : unselectedText;
    }

    private void UpdateHeaderCopy()
    {
        var selectedCount = _showingOutward ? _outwardJobCount : _inwardJobCount;
        JobsCountLabel.Text = selectedCount == 1 ? "1 job" : $"{selectedCount} jobs";
        JobsSubtitleLabel.Text = _showingOutward
            ? "Review the outward work assigned to you."
            : "Review the inward work assigned to you.";
    }

    private void OnJobsModeSwitchTapped(object? sender, EventArgs e)
    {
        if (_showingOutward)
        {
            OnInwardTabClicked(sender, e);
        }
        else
        {
            OnOutwardTabClicked(sender, e);
        }
    }

    private async void OnJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: InwardJob job })
        {
            return;
        }

        await Shell.Current.GoToAsync($"JobDetailPage?id={job.Id}");
    }

    private async void OnOutwardJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: OutwardJob job })
        {
            return;
        }

        // Docked/Loading jobs are exactly the ones Plan & Load (3D) can actually work with -
        // skip the legacy detail screen and go straight there. Earlier stages (PickListGenerated/
        // Assigned) still need it first for Dock-In; Completed jobs still need it for review.
        var route = job.Status is "Docked" or "Loading"
            ? $"{nameof(LoadPlanEditorPage)}?id={job.Id}"
            : $"OutwardJobDetailPage?id={job.Id}";
        await Shell.Current.GoToAsync(route);
    }
}

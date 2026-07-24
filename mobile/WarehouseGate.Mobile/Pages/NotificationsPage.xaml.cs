using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class NotificationsPage : ContentPage
{
    // Mirrors SecurityDashboardPage's threshold so the two surfaces agree on what "waiting too long" means.
    private const int GateWaitAlertThresholdMinutes = 30;
    private bool _isLoading;

    public NotificationsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Shell.SetFlyoutBehavior(this, Session.IsSecurity || Session.IsSupervisor ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled);
        SupervisorNavBar.IsVisible = false;
        HeaderView.AccountRoute = Session.IsSupervisor
            ? "//SupervisorTabs/SupervisorAccountPage"
            : "//SecurityTabs/SecurityAccountPage";

        SupervisorHubClient.JobUpdated += OnHubChanged;
        SupervisorHubClient.JobAssignedToYou += OnHubChanged;
        SupervisorHubClient.OutwardJobUpdated += OnHubOutwardChanged;
        SupervisorHubClient.OutwardJobAssignedToYou += OnHubOutwardChanged;

        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobUpdated -= OnHubChanged;
        SupervisorHubClient.JobAssignedToYou -= OnHubChanged;
        SupervisorHubClient.OutwardJobUpdated -= OnHubOutwardChanged;
        SupervisorHubClient.OutwardJobAssignedToYou -= OnHubOutwardChanged;
    }

    private void OnHubChanged(InwardJob job) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());
    private void OnHubOutwardChanged(OutwardJob job) => MainThread.BeginInvokeOnMainThread(() => _ = LoadAsync());

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
            if (Session.IsSecurity)
            {
                await LoadSecurityNotificationsAsync();
            }
            else if (Session.IsSupervisor)
            {
                await LoadSupervisorNotificationsAsync();
            }
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

    private async Task LoadSecurityNotificationsAsync()
    {
        var activeTask = ApiClient.GetSecurityTransactionsAsync(activeOnly: true);
        var pendingExitTask = ApiClient.GetPendingExitJobsAsync();
        var completedTodayTask = ApiClient.GetSecurityTransactionsAsync(activeOnly: false, date: DateTime.Today);
        await Task.WhenAll(activeTask, pendingExitTask, completedTodayTask);

        var active = activeTask.Result;
        var pendingExit = pendingExitTask.Result;
        var completedToday = completedTodayTask.Result;

        var waitingTooLong = active.Count(j =>
            j.Status == "GateIn" && (DateTime.UtcNow - j.GateInTime).TotalMinutes > GateWaitAlertThresholdMinutes);
        var readyToExit = pendingExit.Count;
        var newVehicles = active.Count(j => j.IsNewVehicle);
        var deliveryMismatches = active.Count(j => j.HasDeliveryDateMismatch);
        var exceptionsToday = completedToday.Count(j => j.Grn?.HasExceptions == true);

        NotificationContainer.Children.Clear();

        if (waitingTooLong > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.Clock, "StatusException", waitingTooLong,
                waitingTooLong == 1 ? "vehicle waiting over 30 min at the gate" : "vehicles waiting over 30 min at the gate",
                "//SecurityTabs/SecurityStatusPage"));
        }
        if (readyToExit > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.RightFromBracket, "StatusSuccess", readyToExit,
                readyToExit == 1 ? "vehicle ready to exit" : "vehicles ready to exit",
                "//SecurityTabs/VehicleExitPage"));
        }
        if (newVehicles > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.TriangleExclamation, "StatusAssigned", newVehicles,
                newVehicles == 1 ? "new vehicle flagged for office review" : "new vehicles flagged for office review",
                "//SecurityTabs/SecurityStatusPage"));
        }
        if (deliveryMismatches > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.CalendarDays, "StatusException", deliveryMismatches,
                deliveryMismatches == 1 ? "vehicle outside expected delivery window" : "vehicles outside expected delivery window",
                "//SecurityTabs/SecurityStatusPage"));
        }
        if (exceptionsToday > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.TriangleExclamation, "StatusException", exceptionsToday,
                exceptionsToday == 1 ? "vehicle completed with exceptions today — needs follow-up" : "vehicles completed with exceptions today — needs follow-up",
                "//SecurityTabs/SecurityStatusPage"));
        }

        ClearButton.IsVisible = NotificationContainer.Children.Count > 0;
        if (NotificationContainer.Children.Count == 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAllClearCard());
        }

        NotificationCenter.SetCount(waitingTooLong + readyToExit + newVehicles + deliveryMismatches + exceptionsToday);
    }

    private async Task LoadSupervisorNotificationsAsync()
    {
        var mineInwardTask = ApiClient.GetMyJobsAsync();
        var mineOutwardTask = ApiClient.GetMyOutwardJobsAsync();
        await Task.WhenAll(mineInwardTask, mineOutwardTask);

        // Office now assigns work directly rather than supervisors self-claiming from a shared
        // pool - "needs your attention" means assigned-to-you-but-not-started, not "available".
        var assignedInward = mineInwardTask.Result.Count(j => j.Status == "Assigned");
        var assignedOutward = mineOutwardTask.Result.Count(j => j.Status == "Assigned");

        NotificationContainer.Children.Clear();

        if (assignedInward > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.Truck, "StatusAssigned", assignedInward,
                assignedInward == 1 ? "inward job assigned, not yet started" : "inward jobs assigned, not yet started",
                "//SupervisorTabs/SupervisorHomePage"));
        }
        if (assignedOutward > 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAttentionCard(IconGlyphs.BoxesStacked, "StatusAssigned", assignedOutward,
                assignedOutward == 1 ? "pick list assigned, not yet started" : "pick lists assigned, not yet started",
                "//SupervisorTabs/SupervisorHomePage"));
        }

        ClearButton.IsVisible = NotificationContainer.Children.Count > 0;
        if (NotificationContainer.Children.Count == 0)
        {
            NotificationContainer.Children.Add(UiHelpers.BuildAllClearCard("All caught up", "No new assignments waiting on you right now."));
        }

        NotificationCenter.SetCount(assignedInward + assignedOutward);
    }

    private void OnRefreshing(object? sender, EventArgs e) => _ = LoadAsync();

    // Session-local dismiss, not a delete - these cards are recomputed live from current data
    // (no backend notification/read-state to persist against), so this just empties the current
    // view and zeroes the badge; the next real refresh or hub event recomputes genuine counts.
    private void OnClearClicked(object? sender, EventArgs e)
    {
        NotificationContainer.Children.Clear();
        NotificationContainer.Children.Add(Session.IsSupervisor
            ? UiHelpers.BuildAllClearCard("All caught up", "No new assignments waiting on you right now.")
            : UiHelpers.BuildAllClearCard());
        ClearButton.IsVisible = false;
        NotificationCenter.SetCount(0);
    }
}

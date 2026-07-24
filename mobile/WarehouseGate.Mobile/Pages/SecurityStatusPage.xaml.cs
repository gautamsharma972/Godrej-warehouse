using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SecurityStatusPage : ContentPage
{
    private int _inwardSubTab; // 0=Active, 1=Ready to Exit, 2=History
    private bool _dateFilterActive;
    private bool _suppressDateFilterEvent;
    private List<InwardJob> _activeJobs = new();
    private List<InwardJob> _readyToExitJobs = new();
    private List<InwardJob>? _historyResults;
    private int _selectedHistoryStatusTab = 2; // 0=Assigned, 1=Docked, 2=Completed, 3=Other - Completed is the common case for history
    private bool? _isSearchWide;

    private bool _showingOutwardStatus;
    private bool _outwardDateFilterActive;
    private bool _outwardResultsLoaded;

    private bool _readyToExitLoaded;
    private InwardJob? _selectedExitJob;
    private string? _exitPhotoLocalPath;

    private OutwardJob? _selectedOutwardExitJob;
    private string? _outwardExitPhotoLocalPath;
    private bool _isLoadingActive;
    private bool _isSearchingHistory;
    private bool _isSearchingReadyToExit;

    public SecurityStatusPage()
    {
        InitializeComponent();
        UpdateHistoryTabStyles();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = ResponsiveHelper.IsWide(width);
        if (_isSearchWide == wide)
        {
            return;
        }

        _isSearchWide = wide;
        ApplySearchLayout(wide);
    }

    // Tablet: vehicle search + PO number + date + Search button all in one row. Phone: unchanged -
    // vehicle search full-width, PO/date paired below, Search button full-width beneath that.
    private void ApplySearchLayout(bool wide)
    {
        if (wide)
        {
            StatusHeaderGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            };
            StatusHeaderGrid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            Grid.SetRow(StatusHeaderControls, 0);
            Grid.SetColumn(StatusHeaderControls, 1);
            Grid.SetColumnSpan(StatusHeaderControls, 1);
            StatusHeaderControls.HorizontalOptions = LayoutOptions.End;
            InwardSubTabToggle.WidthRequest = 420;
            InwardSubTabToggle.HorizontalOptions = LayoutOptions.End;

            SearchFieldsGrid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            SearchFieldsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.3, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto)
            };

            Grid.SetRow(VehicleSearchSection, 0);
            Grid.SetColumn(VehicleSearchSection, 0);
            Grid.SetColumnSpan(VehicleSearchSection, 1);

            Grid.SetRow(PoNumberSection, 0);
            Grid.SetColumn(PoNumberSection, 1);

            Grid.SetRow(DateSection, 0);
            Grid.SetColumn(DateSection, 2);

            Grid.SetRow(SearchButtonControl, 0);
            Grid.SetColumn(SearchButtonControl, 3);
            Grid.SetColumnSpan(SearchButtonControl, 1);
            SearchButtonControl.VerticalOptions = LayoutOptions.End;
            SearchButtonControl.WidthRequest = 110;

            OutwardSearchFieldsGrid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            OutwardSearchFieldsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.25, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto)
            };

            Grid.SetRow(OutwardVehicleSearchSection, 0);
            Grid.SetColumn(OutwardVehicleSearchSection, 0);
            Grid.SetColumnSpan(OutwardVehicleSearchSection, 1);

            Grid.SetRow(OutwardDoNumberSection, 0);
            Grid.SetColumn(OutwardDoNumberSection, 1);

            Grid.SetRow(OutwardDateSection, 0);
            Grid.SetColumn(OutwardDateSection, 2);

            Grid.SetRow(OutwardSearchButtonControl, 0);
            Grid.SetColumn(OutwardSearchButtonControl, 3);
            Grid.SetColumnSpan(OutwardSearchButtonControl, 1);
            OutwardSearchButtonControl.VerticalOptions = LayoutOptions.End;
            OutwardSearchButtonControl.WidthRequest = 118;
        }
        else
        {
            StatusHeaderGrid.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            StatusHeaderGrid.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            };
            Grid.SetRow(StatusHeaderControls, 1);
            Grid.SetColumn(StatusHeaderControls, 0);
            Grid.SetColumnSpan(StatusHeaderControls, 1);
            StatusHeaderControls.HorizontalOptions = LayoutOptions.Fill;
            InwardSubTabToggle.WidthRequest = -1;
            InwardSubTabToggle.HorizontalOptions = LayoutOptions.Fill;

            SearchFieldsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)
            };
            SearchFieldsGrid.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)
            };

            Grid.SetRow(VehicleSearchSection, 0);
            Grid.SetColumn(VehicleSearchSection, 0);
            Grid.SetColumnSpan(VehicleSearchSection, 2);

            Grid.SetRow(PoNumberSection, 1);
            Grid.SetColumn(PoNumberSection, 0);
            Grid.SetColumnSpan(PoNumberSection, 1);

            Grid.SetRow(DateSection, 1);
            Grid.SetColumn(DateSection, 1);

            Grid.SetRow(SearchButtonControl, 2);
            Grid.SetColumn(SearchButtonControl, 0);
            Grid.SetColumnSpan(SearchButtonControl, 2);
            SearchButtonControl.VerticalOptions = LayoutOptions.Fill;
            SearchButtonControl.WidthRequest = -1;

            OutwardSearchFieldsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)
            };
            OutwardSearchFieldsGrid.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)
            };

            Grid.SetRow(OutwardVehicleSearchSection, 0);
            Grid.SetColumn(OutwardVehicleSearchSection, 0);
            Grid.SetColumnSpan(OutwardVehicleSearchSection, 2);

            Grid.SetRow(OutwardDoNumberSection, 1);
            Grid.SetColumn(OutwardDoNumberSection, 0);
            Grid.SetColumnSpan(OutwardDoNumberSection, 1);

            Grid.SetRow(OutwardDateSection, 1);
            Grid.SetColumn(OutwardDateSection, 1);

            Grid.SetRow(OutwardSearchButtonControl, 2);
            Grid.SetColumn(OutwardSearchButtonControl, 0);
            Grid.SetColumnSpan(OutwardSearchButtonControl, 2);
            OutwardSearchButtonControl.VerticalOptions = LayoutOptions.Fill;
            OutwardSearchButtonControl.WidthRequest = -1;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        SupervisorHubClient.JobAvailable += OnHubJobChanged;
        SupervisorHubClient.JobClaimed += OnHubJobChanged;
        SupervisorHubClient.JobUpdated += OnHubJobChanged;

        SupervisorHubClient.OutwardJobAvailable += OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobClaimed += OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobUpdated += OnHubOutwardJobChanged;

        _showingOutwardStatus = false;
        UpdateStatusModeStyles();

        _selectedExitJob = null;
        UpdateTabStyles();
        ShowInwardListState();
        _ = LoadActiveAsync();

        _selectedOutwardExitJob = null;
        ShowOutwardListState();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobAvailable -= OnHubJobChanged;
        SupervisorHubClient.JobClaimed -= OnHubJobChanged;
        SupervisorHubClient.JobUpdated -= OnHubJobChanged;

        SupervisorHubClient.OutwardJobAvailable -= OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobClaimed -= OnHubOutwardJobChanged;
        SupervisorHubClient.OutwardJobUpdated -= OnHubOutwardJobChanged;
    }

    private void OnHubJobChanged(InwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_inwardSubTab == 0)
            {
                _ = LoadActiveAsync();
            }
            else if (_inwardSubTab == 1)
            {
                _ = SearchReadyToExitAsync();
            }
            _ = ShowLiveBannerAsync($"Update: {job.VehicleNumber} — {job.Status}");
        });
    }

    private void OnHubOutwardJobChanged(OutwardJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_showingOutwardStatus)
            {
                _ = SearchOutwardAsync();
            }
            _ = ShowLiveBannerAsync($"Update: {job.VehicleNumber} — {job.Status}");
        });
    }

    private void OnStatusInwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutwardStatus = false;
        UpdateStatusModeStyles();
    }

    private void OnStatusOutwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutwardStatus = true;
        UpdateStatusModeStyles();

        _selectedOutwardExitJob = null;
        ShowOutwardListState();

        if (!_outwardResultsLoaded)
        {
            _ = SearchOutwardAsync();
        }
    }

    private void UpdateStatusModeStyles()
    {
        InwardStatusSection.IsVisible = !_showingOutwardStatus;
        OutwardStatusSection.IsVisible = _showingOutwardStatus;
        InwardSubTabToggle.IsVisible = !_showingOutwardStatus && StatusExitConfirmSection.IsVisible == false && StatusExitResultSection.IsVisible == false;
        InwardFilterBar.IsVisible = !_showingOutwardStatus && StatusExitConfirmSection.IsVisible == false && StatusExitResultSection.IsVisible == false;

        var selectedText = (Color)Application.Current!.Resources["Primary"];
        var unselectedText = (Color)Application.Current.Resources["TextSecondaryLight"];

        VehicleModeThumb.HorizontalOptions = _showingOutwardStatus ? LayoutOptions.End : LayoutOptions.Start;
        StatusInwardRadioLabel.TextColor = _showingOutwardStatus ? unselectedText : selectedText;
        StatusOutwardRadioLabel.TextColor = _showingOutwardStatus ? selectedText : unselectedText;
    }

    private void OnVehicleModeSwitchTapped(object? sender, EventArgs e)
    {
        if (_showingOutwardStatus)
        {
            OnStatusInwardTabClicked(sender, e);
        }
        else
        {
            OnStatusOutwardTabClicked(sender, e);
        }
    }

    private async Task SearchOutwardAsync()
    {
        OutwardListSpinner.IsVisible = true;
        OutwardListSpinner.IsRunning = true;
        try
        {
            var vehicleNumber = string.IsNullOrWhiteSpace(OutwardVehicleSearchBar.Text) ? null : OutwardVehicleSearchBar.Text.Trim();
            var doNumber = string.IsNullOrWhiteSpace(OutwardDoNumberSearchEntry.Text) ? null : OutwardDoNumberSearchEntry.Text.Trim();
            DateTime? date = _outwardDateFilterActive ? OutwardHistoryDatePicker.Date : null;

            var results = await ApiClient.GetOutwardSecurityTransactionsAsync(activeOnly: false, vehicleNumber, doNumber, date);
            OutwardStatusCollectionView.ItemsSource = results;
            NoOutwardResultsLabel.IsVisible = results.Count == 0;
            _outwardResultsLoaded = true;
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
            OutwardListSpinner.IsVisible = false;
            OutwardListSpinner.IsRunning = false;
        }
    }

    private async void OnOutwardSearchClicked(object? sender, EventArgs e) => await SearchOutwardAsync();

    private void OnOutwardHistoryDateSelected(object? sender, DateChangedEventArgs e)
    {
        _outwardDateFilterActive = true;
        OutwardClearDateButton.IsVisible = true;
    }

    private void OnOutwardClearDateClicked(object? sender, EventArgs e)
    {
        _outwardDateFilterActive = false;
        OutwardClearDateButton.IsVisible = false;
        OutwardHistoryDatePicker.Date = DateTime.Today;
    }

    private async void OnOutwardStatusJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: OutwardJob job })
        {
            return;
        }

        await Shell.Current.GoToAsync($"OutwardJobDetailPage?id={job.Id}");
    }

    private void ShowOutwardListState()
    {
        OutwardSearchSection.IsVisible = true;
        OutwardExitConfirmSection.IsVisible = false;
        OutwardExitResultSection.IsVisible = false;
    }

    private void ShowOutwardExitConfirmState()
    {
        OutwardSearchSection.IsVisible = false;
        OutwardExitConfirmSection.IsVisible = true;
        OutwardExitResultSection.IsVisible = false;
    }

    private void ShowOutwardExitResultState()
    {
        OutwardSearchSection.IsVisible = false;
        OutwardExitConfirmSection.IsVisible = false;
        OutwardExitResultSection.IsVisible = true;
    }

    private void OnOutwardReadyToExitJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: OutwardJob job })
        {
            return;
        }

        _selectedOutwardExitJob = job;
        _outwardExitPhotoLocalPath = null;
        OutwardExitPhotoStrip.Children.Clear();
        OutwardStatusConfirmExitButton.IsEnabled = false;

        OutwardConfirmVehicleLabel.Text = job.VehicleNumber;
        OutwardConfirmSubtitleLabel.Text = $"DO {job.DispatchOrderNumber} · {job.CustomerName}";
        OutwardConfirmDriverLabel.Text = string.IsNullOrWhiteSpace(job.DriverName) ? "Driver not recorded" : $"Driver: {job.DriverName}";
        OutwardConfirmDispatchNoteLabel.Text = job.DispatchNote is null ? string.Empty : $"Dispatch Note {job.DispatchNote.DispatchNoteNumber}";

        ShowOutwardExitConfirmState();
    }

    private async void OnOutwardStatusCaptureExitPhotoClicked(object? sender, EventArgs e)
    {
        var localPath = await CapturePhotoToLocalCacheAsync();
        if (localPath is null)
        {
            return;
        }

        _outwardExitPhotoLocalPath = localPath;
        OutwardExitPhotoStrip.Children.Clear();
        OutwardExitPhotoStrip.Children.Add(new Image { Source = ImageSource.FromFile(localPath), Aspect = Aspect.AspectFill, WidthRequest = 72, HeightRequest = 56 });
        OutwardStatusConfirmExitButton.IsEnabled = true;
    }

    private async void OnOutwardStatusConfirmExitClicked(object? sender, EventArgs e)
    {
        if (_selectedOutwardExitJob is null || _outwardExitPhotoLocalPath is null)
        {
            return;
        }

        var confirmed = await DisplayAlert("Confirm Exit", $"Confirm vehicle exit for {_selectedOutwardExitJob.VehicleNumber}?", "Confirm", "Cancel");
        if (!confirmed)
        {
            return;
        }

        OutwardStatusConfirmExitButton.IsEnabled = false;
        OutwardStatusExitSpinner.IsVisible = true;
        OutwardStatusExitSpinner.IsRunning = true;

        try
        {
            var job = await ApiClient.RecordOutwardExitAsync(_selectedOutwardExitJob.Id, _outwardExitPhotoLocalPath);
            OutwardStatusResultVehicleLabel.Text = $"{job.VehicleNumber} · DO {job.DispatchOrderNumber}";
            OutwardStatusGatePassTokenLabel.Text = job.GatePassToken;

            ShowOutwardExitResultState();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not record exit", ex.Message, "OK");
            OutwardStatusConfirmExitButton.IsEnabled = true;
        }
        catch (Exception)
        {
            await DisplayAlert("Error", "Could not reach the server.", "OK");
            OutwardStatusConfirmExitButton.IsEnabled = true;
        }
        finally
        {
            OutwardStatusExitSpinner.IsVisible = false;
            OutwardStatusExitSpinner.IsRunning = false;
        }
    }

    private void OnOutwardStatusCancelExitClicked(object? sender, EventArgs e)
    {
        _selectedOutwardExitJob = null;
        ShowOutwardListState();
    }

    private void OnOutwardStatusExitDoneClicked(object? sender, EventArgs e)
    {
        _selectedOutwardExitJob = null;
        ShowOutwardListState();
        _ = SearchOutwardAsync();
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

    private async Task LoadActiveAsync()
    {
        if (_isLoadingActive)
        {
            RefreshViewControl.IsRefreshing = false;
            return;
        }

        _isLoadingActive = true;
        try
        {
            var active = await ApiClient.GetSecurityTransactionsAsync(activeOnly: true);
            _activeJobs = active;
            ApplyActiveFilter();
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
            _isLoadingActive = false;
            RefreshViewControl.IsRefreshing = false;
        }
    }

    private List<InwardJob> ApplyCurrentInwardFilters(IEnumerable<InwardJob> jobs)
    {
        var query = VehicleSearchBar.Text?.Trim();
        var poNumber = PoNumberSearchEntry.Text?.Trim();
        var date = HistoryDatePicker.Date.Date;

        return jobs.Where(job =>
            MatchesSearch(job, query) &&
            MatchesPoFilter(job, poNumber) &&
            (!_dateFilterActive || job.GateInTime.Date == date)).ToList();
    }

    private static bool MatchesSearch(InwardJob job, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return ContainsText(job.VehicleNumber, query) ||
               ContainsText(job.PONumber, query) ||
               ContainsText(job.SupplierName, query) ||
               ContainsText(job.InwardTxnNumber, query);
    }

    private static bool MatchesPoFilter(InwardJob job, string? poNumber)
    {
        if (string.IsNullOrWhiteSpace(poNumber))
        {
            return true;
        }

        return ContainsText(job.PONumber, poNumber) || ContainsText(job.InwardTxnNumber, poNumber);
    }

    private static bool ContainsText(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private void ApplyActiveFilter()
    {
        var filtered = ApplyCurrentInwardFilters(_activeJobs);
        ActiveCollectionView.ItemsSource = filtered;
        NoActiveLabel.IsVisible = filtered.Count == 0;
    }

    private void ApplyReadyToExitFilter()
    {
        var filtered = ApplyCurrentInwardFilters(_readyToExitJobs);
        ReadyToExitCollectionView.ItemsSource = filtered;
        NoReadyToExitLabel.IsVisible = filtered.Count == 0;
    }

    private void ApplyCurrentInwardFilter()
    {
        if (_inwardSubTab == 0)
        {
            ApplyActiveFilter();
        }
        else if (_inwardSubTab == 1)
        {
            ApplyReadyToExitFilter();
        }
    }

    private async Task SearchHistoryAsync()
    {
        if (_isSearchingHistory)
        {
            RefreshViewControl.IsRefreshing = false;
            return;
        }

        _isSearchingHistory = true;
        try
        {
            var vehicleNumber = string.IsNullOrWhiteSpace(VehicleSearchBar.Text) ? null : VehicleSearchBar.Text.Trim();
            var poNumber = string.IsNullOrWhiteSpace(PoNumberSearchEntry.Text) ? null : PoNumberSearchEntry.Text.Trim();
            DateTime? date = _dateFilterActive ? HistoryDatePicker.Date : null;

            _historyResults = await ApiClient.GetSecurityTransactionsAsync(activeOnly: false, vehicleNumber, poNumber, date);
            await RenderHistoryListAsync();
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
            _isSearchingHistory = false;
            RefreshViewControl.IsRefreshing = false;
        }
    }

    // Filters the last search's results (no new API call) to whichever status tab is active.
    // "Other" is a catch-all (GateIn/Inspecting/anything unforeseen) rather than an explicit list.
    // Shows a spinner and hands the filtering off the UI thread first - switching tabs was
    // visibly janky when re-binding the CollectionView happened synchronously right after the tap.
    private async Task RenderHistoryListAsync()
    {
        var results = _historyResults ?? new List<InwardJob>();
        var statusTab = _selectedHistoryStatusTab;

        HistoryCollectionView.IsVisible = false;
        NoHistoryLabel.IsVisible = false;
        HistoryListSpinner.IsVisible = true;
        HistoryListSpinner.IsRunning = true;
        await Task.Yield();

        var filtered = await Task.Run(() => statusTab switch
        {
            0 => results.Where(j => j.Status == "Assigned").ToList(),
            1 => results.Where(j => j.Status == "Docked").ToList(),
            2 => results.Where(j => j.Status == "Completed").ToList(),
            _ => results.Where(j => j.Status is not ("Assigned" or "Docked" or "Completed")).ToList()
        });

        HistoryListSpinner.IsVisible = false;
        HistoryListSpinner.IsRunning = false;
        HistoryCollectionView.ItemsSource = filtered;
        HistoryCollectionView.IsVisible = filtered.Count > 0;
        NoHistoryLabel.IsVisible = filtered.Count == 0;
    }

    private async Task SelectHistoryStatusTabAsync(int index)
    {
        _selectedHistoryStatusTab = index;
        UpdateHistoryTabStyles();
        await RenderHistoryListAsync();
    }

    private void UpdateHistoryTabStyles()
    {
        var activeColor = (Color)Application.Current!.Resources["Primary"];
        var inactiveTextColor = (Color)Application.Current.Resources["TextSecondaryLight"];

        SetHistoryTabState(HistoryAssignedTabLabel, HistoryAssignedTabIndicator, _selectedHistoryStatusTab == 0, activeColor, inactiveTextColor);
        SetHistoryTabState(HistoryDockedTabLabel, HistoryDockedTabIndicator, _selectedHistoryStatusTab == 1, activeColor, inactiveTextColor);
        SetHistoryTabState(HistoryCompletedTabLabel, HistoryCompletedTabIndicator, _selectedHistoryStatusTab == 2, activeColor, inactiveTextColor);
        SetHistoryTabState(HistoryOtherTabLabel, HistoryOtherTabIndicator, _selectedHistoryStatusTab == 3, activeColor, inactiveTextColor);
    }

    private static void SetHistoryTabState(Label label, BoxView indicator, bool active, Color activeColor, Color inactiveColor)
    {
        label.TextColor = active ? activeColor : inactiveColor;
        var color = active ? activeColor : Colors.Transparent;
        indicator.Color = color;
        indicator.BackgroundColor = color;
    }

    private async void OnHistoryAssignedTabClicked(object? sender, EventArgs e) => await SelectHistoryStatusTabAsync(0);
    private async void OnHistoryDockedTabClicked(object? sender, EventArgs e) => await SelectHistoryStatusTabAsync(1);
    private async void OnHistoryCompletedTabClicked(object? sender, EventArgs e) => await SelectHistoryStatusTabAsync(2);
    private async void OnHistoryOtherTabClicked(object? sender, EventArgs e) => await SelectHistoryStatusTabAsync(3);

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        if (_showingOutwardStatus)
        {
            // SearchOutwardAsync manages its own OutwardListSpinner, not RefreshViewControl -
            // reset the pull-to-refresh spinner here once it completes.
            await SearchOutwardAsync();
            RefreshViewControl.IsRefreshing = false;
        }
        else
        {
            _ = _inwardSubTab switch
            {
                1 => SearchReadyToExitAsync(),
                2 => SearchHistoryAsync(),
                _ => LoadActiveAsync()
            };
        }
    }

    private void OnHistoryDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (_suppressDateFilterEvent)
        {
            return;
        }

        _dateFilterActive = true;
        ClearDateButton.IsVisible = true;

        if (_inwardSubTab == 2)
        {
            _ = SearchHistoryAsync();
        }
        else
        {
            ApplyCurrentInwardFilter();
        }
    }

    private void OnClearDateClicked(object? sender, EventArgs e)
    {
        _dateFilterActive = false;
        ClearDateButton.IsVisible = false;

        _suppressDateFilterEvent = true;
        try
        {
            HistoryDatePicker.Date = DateTime.Today;
        }
        finally
        {
            _suppressDateFilterEvent = false;
        }

        if (_inwardSubTab == 2)
        {
            _ = SearchHistoryAsync();
        }
        else
        {
            ApplyCurrentInwardFilter();
        }
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        if (_inwardSubTab == 2)
        {
            await SearchHistoryAsync();
            return;
        }

        ApplyCurrentInwardFilter();
    }

    private void OnFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_inwardSubTab != 2)
        {
            ApplyCurrentInwardFilter();
        }
    }

    private void OnPoNumberFocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(PoNumberEntryBorder, true);
    private void OnPoNumberUnfocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(PoNumberEntryBorder, false);

    private void OnOutwardDoNumberFocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(OutwardDoNumberEntryBorder, true);
    private void OnOutwardDoNumberUnfocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(OutwardDoNumberEntryBorder, false);

    private void OnActiveTabClicked(object? sender, EventArgs e)
    {
        _inwardSubTab = 0;
        UpdateTabStyles();
        ShowInwardListState();
    }

    private void OnReadyToExitTabClicked(object? sender, EventArgs e)
    {
        _inwardSubTab = 1;
        UpdateTabStyles();
        ShowInwardListState();
        if (!_readyToExitLoaded)
        {
            _ = SearchReadyToExitAsync();
        }
    }

    private void OnHistoryTabClicked(object? sender, EventArgs e)
    {
        _inwardSubTab = 2;
        UpdateTabStyles();
        ShowInwardListState();
        if (_historyResults is null)
        {
            _ = SearchHistoryAsync();
        }
    }

    private void UpdateTabStyles()
    {
        var selectedBg = (Color)Application.Current!.Resources["Primary"];
        var selectedText = (Color)Application.Current.Resources["PrimaryDarkText"];
        var unselectedText = (Color)Application.Current.Resources["TextSecondaryLight"];

        SetSubTabButtonState(ActiveTabButton, _inwardSubTab == 0, selectedBg, selectedText, unselectedText);
        SetSubTabButtonState(ReadyToExitTabButton, _inwardSubTab == 1, selectedBg, selectedText, unselectedText);
        SetSubTabButtonState(HistoryTabButton, _inwardSubTab == 2, selectedBg, selectedText, unselectedText);

        VehicleSearchBar.Placeholder = _inwardSubTab == 2 ? "Vehicle number" : "Vehicle number, PO, supplier";
        SearchButtonControl.Text = _inwardSubTab == 2 ? "Search" : "Apply Filters";
    }

    private static void SetSubTabButtonState(Button button, bool active, Color selectedBg, Color selectedText, Color unselectedText)
    {
        button.BackgroundColor = active ? selectedBg : Colors.Transparent;
        button.TextColor = active ? selectedText : unselectedText;
    }

    private void ShowInwardListState()
    {
        InwardSubTabToggle.IsVisible = true;
        InwardFilterBar.IsVisible = true;
        ActiveSection.IsVisible = _inwardSubTab == 0;
        ReadyToExitSection.IsVisible = _inwardSubTab == 1;
        HistorySection.IsVisible = _inwardSubTab == 2;
        StatusExitConfirmSection.IsVisible = false;
        StatusExitResultSection.IsVisible = false;
    }

    private void ShowExitConfirmState()
    {
        InwardSubTabToggle.IsVisible = false;
        InwardFilterBar.IsVisible = false;
        ActiveSection.IsVisible = false;
        ReadyToExitSection.IsVisible = false;
        HistorySection.IsVisible = false;
        StatusExitConfirmSection.IsVisible = true;
        StatusExitResultSection.IsVisible = false;
    }

    private void ShowExitResultState()
    {
        InwardSubTabToggle.IsVisible = false;
        InwardFilterBar.IsVisible = false;
        ActiveSection.IsVisible = false;
        ReadyToExitSection.IsVisible = false;
        HistorySection.IsVisible = false;
        StatusExitConfirmSection.IsVisible = false;
        StatusExitResultSection.IsVisible = true;
    }

    private async Task SearchReadyToExitAsync()
    {
        if (_isSearchingReadyToExit)
        {
            RefreshViewControl.IsRefreshing = false;
            return;
        }

        _isSearchingReadyToExit = true;
        try
        {
            var results = await ApiClient.GetPendingExitJobsAsync();
            _readyToExitJobs = results;
            ApplyReadyToExitFilter();
            _readyToExitLoaded = true;
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
            _isSearchingReadyToExit = false;
            RefreshViewControl.IsRefreshing = false;
        }
    }

    private void OnReadyToExitJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: InwardJob job })
        {
            return;
        }

        _selectedExitJob = job;
        _exitPhotoLocalPath = null;
        StatusExitPhotoStrip.Children.Clear();
        StatusConfirmExitButton.IsEnabled = false;

        StatusConfirmVehicleLabel.Text = job.VehicleNumber;
        StatusConfirmSubtitleLabel.Text = job.Subtitle;
        StatusConfirmDriverLabel.Text = string.IsNullOrWhiteSpace(job.DriverName) ? "Driver not recorded" : $"Driver: {job.DriverName}";
        StatusConfirmGrnLabel.Text = job.Grn is null ? string.Empty : $"GRN {job.Grn.GrnNumber}";

        ShowExitConfirmState();
    }

    private async void OnStatusCaptureExitPhotoClicked(object? sender, EventArgs e)
    {
        var localPath = await CapturePhotoToLocalCacheAsync();
        if (localPath is null)
        {
            return;
        }

        _exitPhotoLocalPath = localPath;
        StatusExitPhotoStrip.Children.Clear();
        StatusExitPhotoStrip.Children.Add(new Image { Source = ImageSource.FromFile(localPath), Aspect = Aspect.AspectFill, WidthRequest = 72, HeightRequest = 56 });
        StatusConfirmExitButton.IsEnabled = true;
    }

    private async Task<string?> CapturePhotoToLocalCacheAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not supported", "This device does not support photo capture.", "OK");
                return null;
            }

            return await PhotoCapture.CaptureAndSaveAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Camera error", ex.Message, "OK");
            return null;
        }
    }

    private async void OnStatusConfirmExitClicked(object? sender, EventArgs e)
    {
        if (_selectedExitJob is null || _exitPhotoLocalPath is null)
        {
            return;
        }

        var confirmed = await DisplayAlert("Confirm Exit", $"Confirm vehicle exit for {_selectedExitJob.VehicleNumber}?", "Confirm", "Cancel");
        if (!confirmed)
        {
            return;
        }

        StatusConfirmExitButton.IsEnabled = false;
        StatusExitSpinner.IsVisible = true;
        StatusExitSpinner.IsRunning = true;

        try
        {
            var job = await ApiClient.RecordExitAsync(_selectedExitJob.Id, _exitPhotoLocalPath);
            StatusResultVehicleLabel.Text = string.IsNullOrWhiteSpace(job.PONumber) ? job.VehicleNumber : $"{job.VehicleNumber} · PO {job.PONumber}";
            StatusGatePassTokenLabel.Text = job.GatePassToken;

            ShowExitResultState();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not record exit", ex.Message, "OK");
            StatusConfirmExitButton.IsEnabled = true;
        }
        catch (Exception)
        {
            await DisplayAlert("Error", "Could not reach the server.", "OK");
            StatusConfirmExitButton.IsEnabled = true;
        }
        finally
        {
            StatusExitSpinner.IsVisible = false;
            StatusExitSpinner.IsRunning = false;
        }
    }

    private void OnStatusCancelExitClicked(object? sender, EventArgs e)
    {
        _selectedExitJob = null;
        ShowInwardListState();
    }

    private void OnStatusExitDoneClicked(object? sender, EventArgs e)
    {
        _selectedExitJob = null;
        ShowInwardListState();
        _ = SearchReadyToExitAsync();
    }

    private async void OnJobTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: InwardJob job })
        {
            return;
        }

        await Shell.Current.GoToAsync($"JobDetailPage?id={job.Id}");
    }
}

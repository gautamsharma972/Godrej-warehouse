using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SupervisorHistoryPage : ContentPage
{
    private bool _showingOutward;
    private bool _dateFilterActive;
    private bool? _isSearchWide;
    private int? _inwardResultCount;
    private int? _outwardResultCount;
    private bool _isSearching;

    public SupervisorHistoryPage()
    {
        InitializeComponent();
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

    private void ApplySearchLayout(bool wide)
    {
        if (wide)
        {
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

            Grid.SetRow(OrderNumberSection, 0);
            Grid.SetColumn(OrderNumberSection, 1);

            Grid.SetRow(DateSection, 0);
            Grid.SetColumn(DateSection, 2);

            Grid.SetRow(SearchButtonControl, 0);
            Grid.SetColumn(SearchButtonControl, 3);
            Grid.SetColumnSpan(SearchButtonControl, 1);
            SearchButtonControl.VerticalOptions = LayoutOptions.End;
            SearchButtonControl.WidthRequest = 128;
        }
        else
        {
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

            Grid.SetRow(OrderNumberSection, 1);
            Grid.SetColumn(OrderNumberSection, 0);
            Grid.SetColumnSpan(OrderNumberSection, 1);

            Grid.SetRow(DateSection, 1);
            Grid.SetColumn(DateSection, 1);

            Grid.SetRow(SearchButtonControl, 2);
            Grid.SetColumn(SearchButtonControl, 0);
            Grid.SetColumnSpan(SearchButtonControl, 2);
            SearchButtonControl.VerticalOptions = LayoutOptions.Fill;
            SearchButtonControl.WidthRequest = -1;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateTabStyles();
        _ = SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_isSearching)
        {
            RefreshViewControl.IsRefreshing = false;
            return;
        }

        _isSearching = true;
        try
        {
            var vehicleNumber = string.IsNullOrWhiteSpace(VehicleSearchBar.Text) ? null : VehicleSearchBar.Text.Trim();
            var orderNumber = string.IsNullOrWhiteSpace(OrderNumberSearchEntry.Text) ? null : OrderNumberSearchEntry.Text.Trim();
            DateTime? date = _dateFilterActive ? HistoryDatePicker.Date : null;

            if (_showingOutward)
            {
                var results = await ApiClient.GetOutwardHistoryAsync(vehicleNumber, orderNumber, date);
                OutwardResultsCollectionView.ItemsSource = results;
                _outwardResultCount = results.Count;
                ResultsCountLabel.Text = results.Count == 1 ? "1 result" : $"{results.Count} results";
                UpdateModeSwitchText();
            }
            else
            {
                var results = await ApiClient.GetInwardHistoryAsync(vehicleNumber, orderNumber, date);
                InwardResultsCollectionView.ItemsSource = results;
                _inwardResultCount = results.Count;
                ResultsCountLabel.Text = results.Count == 1 ? "1 result" : $"{results.Count} results";
                UpdateModeSwitchText();
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
            _isSearching = false;
            RefreshViewControl.IsRefreshing = false;
        }
    }

    private void OnRefreshing(object? sender, EventArgs e) => _ = SearchAsync();

    private void OnHistoryDateSelected(object? sender, DateChangedEventArgs e)
    {
        _dateFilterActive = true;
        ClearDateButton.IsVisible = true;
    }

    private void OnClearDateClicked(object? sender, EventArgs e)
    {
        _dateFilterActive = false;
        ClearDateButton.IsVisible = false;
        HistoryDatePicker.Date = DateTime.Today;
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await SearchAsync();

    private void OnInwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutward = false;
        UpdateTabStyles();
        _ = SearchAsync();
    }

    private void OnOutwardTabClicked(object? sender, EventArgs e)
    {
        _showingOutward = true;
        UpdateTabStyles();
        _ = SearchAsync();
    }

    private void OnModeSwitchTapped(object? sender, EventArgs e)
    {
        _showingOutward = !_showingOutward;
        UpdateTabStyles();
        _ = SearchAsync();
    }

    private void UpdateTabStyles()
    {
        InwardResultsCollectionView.IsVisible = !_showingOutward;
        OutwardResultsCollectionView.IsVisible = _showingOutward;
        OrderNumberLabel.Text = _showingOutward ? "DO NUMBER" : "PO NUMBER";
        HistorySubtitleLabel.Text = _showingOutward
            ? "Search completed outward dock activity."
            : "Search completed inward dock activity.";
        ModeHintLabel.Text = _showingOutward
            ? "Use vehicle, DO number, or date to narrow completed outward jobs."
            : "Use vehicle, PO number, or date to narrow completed inward jobs.";
        ResultsTitleLabel.Text = _showingOutward ? "Completed outward jobs" : "Completed inward jobs";
        ResultModeLabel.Text = _showingOutward ? "Outward" : "Inward";

        var selectedText = (Color)Application.Current!.Resources["Primary"];
        var unselectedText = (Color)Application.Current.Resources["TextSecondaryLight"];

        InwardSwitchLabel.TextColor = _showingOutward ? unselectedText : selectedText;
        OutwardSwitchLabel.TextColor = _showingOutward ? selectedText : unselectedText;
        HistoryModeSwitchThumb.HorizontalOptions = _showingOutward ? LayoutOptions.End : LayoutOptions.Start;
        UpdateModeSwitchText();
    }

    private void UpdateModeSwitchText()
    {
        InwardSwitchLabel.Text = _inwardResultCount is null ? "Inward" : $"Inward ({_inwardResultCount})";
        OutwardSwitchLabel.Text = _outwardResultCount is null ? "Outward" : $"Outward ({_outwardResultCount})";
    }

    private async void OnInwardJobTapped(object? sender, EventArgs e)
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

        await Shell.Current.GoToAsync($"OutwardJobDetailPage?id={job.Id}");
    }
}

using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class TransporterPickerPage : ContentPage
{
    private readonly Action<string?> _onResult;
    private readonly IReadOnlyList<string> _transporters;
    private bool _resultSent;
    private string _currentQuery = string.Empty;

    // transporters defaults to the hardcoded demo list (still used by the Outward gate check-in
    // flow) - the Inward Gate Check-in screen passes the real Transporter master instead (see
    // GateController.GetTransporters / ApiClient.GetTransportersAsync).
    public TransporterPickerPage(Action<string?> onResult, IReadOnlyList<string>? transporters = null)
    {
        InitializeComponent();
        _onResult = onResult;
        _transporters = transporters ?? VehicleLookupService.KnownTransporters;
        ApplyFilter(string.Empty);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter(e.NewTextValue ?? string.Empty);

    private void ApplyFilter(string query)
    {
        _currentQuery = (query ?? string.Empty).Trim();
        var results = string.IsNullOrWhiteSpace(_currentQuery)
            ? _transporters
            : _transporters.Where(t => t.Contains(_currentQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        ResultsCollectionView.ItemsSource = results;
        NoResultsLabel.IsVisible = results.Count == 0;
        var hasExactMatch = _transporters.Any(t => string.Equals(t, _currentQuery, StringComparison.OrdinalIgnoreCase));
        UseTypedTransporterCard.IsVisible = _currentQuery.Length > 0 && !hasExactMatch;
        if (UseTypedTransporterCard.IsVisible)
        {
            UseTypedTransporterLabel.Text = _currentQuery;
        }
    }

    private async void OnUseTypedTransporterTapped(object? sender, EventArgs e)
    {
        if (_resultSent || _currentQuery.Length == 0)
        {
            return;
        }

        _resultSent = true;
        _onResult(_currentQuery);
        await Navigation.PopModalAsync();
    }

    private async void OnItemTapped(object? sender, EventArgs e)
    {
        if (_resultSent || sender is not Border { BindingContext: string transporter })
        {
            return;
        }

        _resultSent = true;
        _onResult(transporter);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_resultSent)
        {
            return;
        }

        _resultSent = true;
        _onResult(null);
        await Navigation.PopModalAsync();
    }
}

using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class TransporterPickerPage : ContentPage
{
    private readonly Action<string?> _onResult;
    private bool _resultSent;

    public TransporterPickerPage(Action<string?> onResult)
    {
        InitializeComponent();
        _onResult = onResult;
        ApplyFilter(string.Empty);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter(e.NewTextValue ?? string.Empty);

    private void ApplyFilter(string query)
    {
        var results = string.IsNullOrWhiteSpace(query)
            ? VehicleLookupService.KnownTransporters
            : VehicleLookupService.KnownTransporters
                .Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        ResultsCollectionView.ItemsSource = results;
        NoResultsLabel.IsVisible = results.Length == 0;
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

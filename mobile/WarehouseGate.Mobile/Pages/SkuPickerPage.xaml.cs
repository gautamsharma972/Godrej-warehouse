using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SkuPickerPage : ContentPage
{
    private readonly Action<SkuMasterItem?> _onResult;
    private bool _resultSent;
    private int _searchGeneration;

    public SkuPickerPage(Action<SkuMasterItem?> onResult)
    {
        InitializeComponent();
        _onResult = onResult;
        _ = ApplyFilterAsync(string.Empty);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => _ = ApplyFilterAsync(e.NewTextValue ?? string.Empty);

    // Server-side search (SKU Master can be large, unlike the hardcoded Transporter demo list) -
    // _searchGeneration discards a stale response if the user keeps typing before an earlier
    // request returns, so results never flicker back to an outdated query's list.
    private async Task ApplyFilterAsync(string query)
    {
        var generation = ++_searchGeneration;
        try
        {
            var results = await ApiClient.SearchSkuMasterAsync(query);
            if (generation != _searchGeneration)
            {
                return;
            }

            ResultsCollectionView.ItemsSource = results;
            NoResultsLabel.IsVisible = results.Count == 0;
        }
        catch (Exception)
        {
            if (generation == _searchGeneration)
            {
                ResultsCollectionView.ItemsSource = Array.Empty<SkuMasterItem>();
                NoResultsLabel.IsVisible = true;
            }
        }
    }

    private async void OnItemTapped(object? sender, EventArgs e)
    {
        if (_resultSent || sender is not Border { BindingContext: SkuMasterItem sku })
        {
            return;
        }

        _resultSent = true;
        _onResult(sku);
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

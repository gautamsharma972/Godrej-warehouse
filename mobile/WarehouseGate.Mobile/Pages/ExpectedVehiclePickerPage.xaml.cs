using WarehouseGate.Mobile.Models;

namespace WarehouseGate.Mobile.Pages;

public partial class ExpectedVehiclePickerPage : ContentPage
{
    private readonly List<VehicleOption> _allOptions;
    private readonly Action<string?> _onResult;
    private bool _resultSent;
    private bool _opened;
    private string _currentQuery = string.Empty;

    public ExpectedVehiclePickerPage(List<VehicleOption> options, Action<string?> onResult)
    {
        InitializeComponent();
        _allOptions = options;
        _onResult = onResult;
        ApplyFilter(string.Empty);
    }

    protected override async void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (_opened || width <= 0)
        {
            return;
        }

        _opened = true;
        DrawerPanel.TranslationX = width * 0.7;
        await DrawerPanel.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => ApplyFilter(e.NewTextValue ?? string.Empty);

    private void ApplyFilter(string query)
    {
        _currentQuery = (query ?? string.Empty).Trim();

        var results = _currentQuery.Length == 0
            ? _allOptions
            : _allOptions.Where(o => o.VehicleNumber.Contains(_currentQuery, StringComparison.OrdinalIgnoreCase)).ToList();

        ResultsCollectionView.ItemsSource = results;
        NoResultsLabel.IsVisible = results.Count == 0;

        // Lets a genuinely new vehicle (not yet in the registry/expected list) still be used - the
        // search box was otherwise a dead end once nothing matched, with no way back to free-text
        // entry (the underlying Entry stays hidden while this picker is open).
        var hasExactMatch = results.Any(o => string.Equals(o.VehicleNumber, _currentQuery, StringComparison.OrdinalIgnoreCase));
        UseTypedVehicleCard.IsVisible = _currentQuery.Length > 0 && !hasExactMatch;
        if (UseTypedVehicleCard.IsVisible)
        {
            UseTypedVehicleLabel.Text = _currentQuery.ToUpperInvariant();
        }
    }

    private async void OnUseTypedVehicleTapped(object? sender, EventArgs e)
    {
        if (_resultSent || _currentQuery.Length == 0)
        {
            return;
        }

        _resultSent = true;
        _onResult(_currentQuery.ToUpperInvariant());
        await CloseAsync();
    }

    private async void OnItemTapped(object? sender, EventArgs e)
    {
        if (_resultSent || sender is not Border { BindingContext: VehicleOption option })
        {
            return;
        }

        _resultSent = true;
        _onResult(option.VehicleNumber);
        await CloseAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CancelAsync();
    }

    private async void OnBackdropTapped(object? sender, EventArgs e)
    {
        await CancelAsync();
    }

    private async Task CancelAsync()
    {
        if (_resultSent)
        {
            return;
        }

        _resultSent = true;
        _onResult(null);
        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        await DrawerPanel.TranslateTo(Width * 0.7, 0, 140, Easing.CubicIn);
        await Navigation.PopModalAsync();
    }
}

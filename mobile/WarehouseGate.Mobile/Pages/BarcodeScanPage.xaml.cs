using ZXing.Net.Maui;

namespace WarehouseGate.Mobile.Pages;

public partial class BarcodeScanPage : ContentPage
{
    private readonly Action<string?> _onResult;
    private bool _resultSent;

    public BarcodeScanPage(Action<string?> onResult)
    {
        InitializeComponent();
        _onResult = onResult;
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(value) || _resultSent)
        {
            return;
        }

        _resultSent = true;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _onResult(value);
            await Navigation.PopModalAsync();
        });
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

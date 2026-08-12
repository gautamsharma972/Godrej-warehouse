using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class LoadSimulationPage : ContentPage
{
    private int _sendCount;

    public LoadSimulationPage()
    {
        InitializeComponent();
        SimulationWebView.SetInvokeJavaScriptTarget(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        SetLandscapeOrientation();
        StartPayloadBurst();
    }

    protected override void OnDisappearing()
    {
        SetPortraitOrientation();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Flyout);
        base.OnDisappearing();
    }

    private void StartPayloadBurst()
    {
        _sendCount = 0;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(350), () =>
        {
            _sendCount++;
            if (!string.IsNullOrWhiteSpace(LoadSimulationSession.Payload))
            {
                SimulationWebView.SendRawMessage(LoadSimulationSession.Payload);
            }

            return _sendCount < 8 && Window is not null;
        });
    }

    private async void OnBackTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    private static void SetLandscapeOrientation()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape;
        }
#endif
    }

    private static void SetPortraitOrientation()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is not null)
        {
            activity.RequestedOrientation = Android.Content.PM.ScreenOrientation.SensorPortrait;
        }
#endif
    }
}

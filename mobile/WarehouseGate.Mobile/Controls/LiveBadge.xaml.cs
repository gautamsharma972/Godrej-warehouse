using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

// Self-contained live/reconnecting pill tied to SupervisorHubClient's connection state - was
// previously hand-duplicated (markup + logic) between AppHeaderView and NotificationsPage.
public partial class LiveBadge : ContentView
{
    public LiveBadge()
    {
        InitializeComponent();

        SetConnectionBadge(SupervisorHubClient.IsConnected);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        SetConnectionBadge(SupervisorHubClient.IsConnected);
        SupervisorHubClient.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private void OnUnloaded(object? sender, EventArgs e) =>
        SupervisorHubClient.ConnectionStateChanged -= OnConnectionStateChanged;

    private void OnConnectionStateChanged(bool connected) =>
        MainThread.BeginInvokeOnMainThread(() => SetConnectionBadge(connected));

    private void SetConnectionBadge(bool connected)
    {
        var color = (Color)Application.Current!.Resources[connected ? "StatusSuccess" : "StatusNeutral"];
        LiveDot.Fill = color;
        LiveStatusLabel.TextColor = color;
        LiveStatusLabel.Text = connected ? "Live" : "Reconnecting…";
        Badge.BackgroundColor = color.WithAlpha(0.15f);
    }
}

using WarehouseGate.Mobile.Pages;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public partial class AppHeaderView : ContentView
{
    public static readonly BindableProperty AccountRouteProperty =
        BindableProperty.Create(nameof(AccountRoute), typeof(string), typeof(AppHeaderView));

    public string? AccountRoute
    {
        get => (string?)GetValue(AccountRouteProperty);
        set => SetValue(AccountRouteProperty, value);
    }

    public AppHeaderView()
    {
        InitializeComponent();

        GreetingLabel.Text = DateTime.Now.Hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _ => "Good evening"
        };
        UserNameLabel.Text = Session.DisplayName;

        SetNotificationBadge(NotificationCenter.Count);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        SetNotificationBadge(NotificationCenter.Count);
        NotificationCenter.CountChanged += OnNotificationCountChanged;
    }

    private void OnUnloaded(object? sender, EventArgs e) =>
        NotificationCenter.CountChanged -= OnNotificationCountChanged;

    private void OnNotificationCountChanged(int count) =>
        MainThread.BeginInvokeOnMainThread(() => SetNotificationBadge(count));

    private void SetNotificationBadge(int count)
    {
        NotificationCountBadge.IsVisible = count > 0;
        NotificationCountLabel.Text = count > 9 ? "9+" : count.ToString();
    }

    private async void OnAccountIconTapped(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(AccountRoute))
        {
            await Shell.Current.GoToAsync(AccountRoute);
        }
    }

    private async void OnNotificationsIconTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));
}

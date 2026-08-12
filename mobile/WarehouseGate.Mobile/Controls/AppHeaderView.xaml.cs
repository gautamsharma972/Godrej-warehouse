using WarehouseGate.Mobile.Pages;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public partial class AppHeaderView : ContentView
{
    // Below this, the row of 4 fixed-size icon buttons (menu/notifications/account) plus the
    // Live badge leaves too little of the Star column for the greeting/name text - narrower than
    // ResponsiveHelper.TabletBreakpoint because this header is one embedded row, not a whole page.
    private const double CompactBreakpoint = 420;
    private bool? _isCompact;

    public static readonly BindableProperty AccountRouteProperty =
        BindableProperty.Create(nameof(AccountRoute), typeof(string), typeof(AppHeaderView));

    public static readonly BindableProperty IsDockedProperty =
        BindableProperty.Create(nameof(IsDocked), typeof(bool), typeof(AppHeaderView), false,
            propertyChanged: static (bindable, _, value) =>
                ((AppHeaderView)bindable).ApplySurfaceStyle((bool)value));

    public string? AccountRoute
    {
        get => (string?)GetValue(AccountRouteProperty);
        set => SetValue(AccountRouteProperty, value);
    }

    public bool IsDocked
    {
        get => (bool)GetValue(IsDockedProperty);
        set => SetValue(IsDockedProperty, value);
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

    private void ApplySurfaceStyle(bool docked)
    {
        HeaderSurface.StrokeShape = docked
            ? new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(0) }
            : new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) };
        HeaderSurface.StrokeThickness = docked ? 0 : 1;
        HeaderSurface.Padding = docked ? new Thickness(16, 10) : new Thickness(16, 10);
        HeaderSurface.Shadow = docked ? null : new Shadow
        {
            Brush = Color.FromArgb("#23344F"),
            Offset = new Point(0, 6),
            Radius = 18,
            Opacity = 0.05f
        };
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var compact = width > 0 && width < CompactBreakpoint;
        if (_isCompact == compact)
        {
            return;
        }

        _isCompact = compact;

        // Drop the "Live" badge first - it's the least essential element - to give the
        // greeting/name column enough room to truncate cleanly instead of wrapping character by
        // character. The WG mark stays; the hamburger already makes it a real (tappable) icon.
        LiveStatusBadge.IsVisible = false;
        HeaderGrid.ColumnSpacing = compact ? 8 : 12;
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

    private void OnMenuIconTapped(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            Shell.Current.FlyoutIsPresented = !Shell.Current.FlyoutIsPresented;
        }
    }

    private async void OnNotificationsIconTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(NotificationsPage));
}

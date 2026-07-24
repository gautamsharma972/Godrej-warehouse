namespace WarehouseGate.Mobile.Controls;

public partial class BackHeaderView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(BackHeaderView), "Back");

    // Optional prominent page title shown next to the back arrow, native-nav-bar style
    // (bold, larger, one line) - used instead of a separate title card lower on the page.
    // Empty by default so existing call sites that only set Title (the small caption) are
    // unaffected.
    public static readonly BindableProperty PageTitleProperty =
        BindableProperty.Create(nameof(PageTitle), typeof(string), typeof(BackHeaderView), string.Empty,
            propertyChanged: OnPageTitleChanged);

    public static readonly BindableProperty BackRouteProperty =
        BindableProperty.Create(nameof(BackRoute), typeof(string), typeof(BackHeaderView), "..");

    public static readonly BindableProperty TrailingContentProperty =
        BindableProperty.Create(nameof(TrailingContent), typeof(View), typeof(BackHeaderView), null,
            propertyChanged: OnTrailingContentChanged);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string PageTitle
    {
        get => (string)GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public string BackRoute
    {
        get => (string)GetValue(BackRouteProperty);
        set => SetValue(BackRouteProperty, value);
    }

    public View? TrailingContent
    {
        get => (View?)GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    private static void OnTrailingContentChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((BackHeaderView)bindable).TrailingContentHost.Content = (View?)newValue;

    private static void OnPageTitleChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((BackHeaderView)bindable).PageTitleLabel.IsVisible = !string.IsNullOrWhiteSpace((string?)newValue);

    public BackHeaderView()
    {
        InitializeComponent();
    }

    private async void OnBackTapped(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(BackRoute);
}

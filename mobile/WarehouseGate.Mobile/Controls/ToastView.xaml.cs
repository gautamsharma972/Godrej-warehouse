using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public enum ToastSeverity
{
    Error,
    Success,
    Info
}

// Top-right, auto-dismissing notification - drop <controls:ToastView x:Name="Toast" /> as the
// last child of a page's root Grid/Layout so it overlays on top, then call Toast.ShowAsync(...)
// instead of an inline error banner that pushes the rest of the form around.
public partial class ToastView : ContentView
{
    private int _showToken;

    public ToastView()
    {
        InitializeComponent();
    }

    public async Task ShowAsync(string message, ToastSeverity severity = ToastSeverity.Error, int durationMs = 3200)
    {
        var (bg, icon) = severity switch
        {
            ToastSeverity.Success => ((Color)Application.Current!.Resources["StatusSuccess"], IconGlyphs.CircleCheck),
            ToastSeverity.Info => ((Color)Application.Current!.Resources["Primary"], IconGlyphs.Bell),
            _ => ((Color)Application.Current!.Resources["StatusException"], IconGlyphs.TriangleExclamation)
        };

        // A second toast arriving while one is still showing takes over immediately rather than
        // queuing behind it - the token guards the first toast's delayed hide from firing late
        // and yanking the second one off screen.
        var token = ++_showToken;

        ToastBorder.BackgroundColor = bg;
        ToastIconLabel.Text = icon;
        ToastIconLabel.TextColor = Colors.White;
        ToastMessageLabel.Text = message;

        ToastBorder.IsVisible = true;
        ToastBorder.Opacity = 0;
        ToastBorder.TranslationY = -14;
        await Task.WhenAll(
            ToastBorder.FadeTo(1, 180),
            ToastBorder.TranslateTo(0, 0, 180, Easing.CubicOut));

        await Task.Delay(durationMs);

        if (token != _showToken)
        {
            return;
        }

        await Task.WhenAll(
            ToastBorder.FadeTo(0, 220),
            ToastBorder.TranslateTo(0, -14, 220, Easing.CubicIn));
        ToastBorder.IsVisible = false;
    }
}

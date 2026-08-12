using Microsoft.Maui.Graphics;

namespace WarehouseGate.Mobile.Controls;

public sealed class CircularProgressView : GraphicsView
{
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(CircularProgressView), 0d,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty ProgressColorProperty = BindableProperty.Create(
        nameof(ProgressColor), typeof(Color), typeof(CircularProgressView), Colors.Teal,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(CircularProgressView), Colors.LightGray,
        propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(
        nameof(StrokeWidth), typeof(float), typeof(CircularProgressView), 4f,
        propertyChanged: OnVisualPropertyChanged);

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Color ProgressColor { get => (Color)GetValue(ProgressColorProperty); set => SetValue(ProgressColorProperty, value); }
    public Color TrackColor { get => (Color)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }
    public float StrokeWidth { get => (float)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public CircularProgressView() => Drawable = new ProgressRingDrawable(this);

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((CircularProgressView)bindable).Invalidate();

    private sealed class ProgressRingDrawable(CircularProgressView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var stroke = Math.Max(1f, owner.StrokeWidth);
            var inset = stroke / 2f + 1f;
            var bounds = new RectF(inset, inset, dirtyRect.Width - inset * 2, dirtyRect.Height - inset * 2);

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeColor = owner.TrackColor;
            canvas.DrawEllipse(bounds);

            var progress = Math.Clamp(owner.Progress, 0d, 1d);
            if (progress <= 0)
            {
                return;
            }

            canvas.StrokeColor = owner.ProgressColor;
            canvas.DrawArc(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                90f, 90f - (float)(progress * 360d), false, false);
        }
    }
}

namespace WarehouseGate.Mobile.Controls;

// Stylized vector illustration for the login page's left panel: a warehouse with a docked
// truck and a gate booth, in the app's muted teal palette. Drawn in a 440x300 logical space
// and scaled to fit, so it stays crisp at any tablet size with no image asset to ship.
public class LoginHeroDrawable : IDrawable
{
    private static readonly Color Teal = Color.FromArgb("#0F9B8E");
    private static readonly Color TealDark = Color.FromArgb("#2E4B47");
    private static readonly Color TealMid = Color.FromArgb("#59B3A8");
    private static readonly Color TealSoft = Color.FromArgb("#BFDEDA");
    private static readonly Color WallLight = Color.FromArgb("#F6FAF9");
    private static readonly Color Ink = Color.FromArgb("#20232B");

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var s = Math.Min(dirtyRect.Width / 440f, dirtyRect.Height / 300f);
        var ox = dirtyRect.Width / 2f - 220f * s;
        var oy = dirtyRect.Height / 2f - 150f * s;

        canvas.SaveState();
        canvas.Translate(ox, oy);
        canvas.Scale(s, s);
        canvas.Antialias = true;

        // Distant city silhouette
        canvas.FillColor = TealSoft.WithAlpha(0.45f);
        canvas.FillRoundedRectangle(300, 120, 26, 90, 3);
        canvas.FillRoundedRectangle(332, 100, 30, 110, 3);
        canvas.FillRoundedRectangle(368, 135, 22, 75, 3);
        canvas.FillRoundedRectangle(20, 140, 24, 70, 3);

        // Clouds
        canvas.FillColor = Colors.White.WithAlpha(0.9f);
        DrawCloud(canvas, 70, 60, 1f);
        DrawCloud(canvas, 330, 40, 0.8f);
        DrawCloud(canvas, 210, 25, 0.55f);

        // Ground
        canvas.FillColor = TealSoft.WithAlpha(0.5f);
        canvas.FillRoundedRectangle(0, 208, 440, 60, 10);
        canvas.FillColor = TealSoft.WithAlpha(0.35f);
        canvas.FillEllipse(30, 250, 380, 40);

        // Warehouse body
        canvas.FillColor = WallLight;
        canvas.FillRectangle(48, 118, 190, 96);
        // Roof
        var roof = new PathF();
        roof.MoveTo(40, 122);
        roof.LineTo(143, 88);
        roof.LineTo(246, 122);
        roof.Close();
        canvas.FillColor = TealMid;
        canvas.FillPath(roof);
        // Facade sign band
        canvas.FillColor = Teal;
        canvas.FillRoundedRectangle(78, 128, 130, 20, 5);
        canvas.FontColor = Colors.White;
        canvas.FontSize = 12;
        canvas.DrawString("WAREHOUSE", 78, 128, 130, 20, HorizontalAlignment.Center, VerticalAlignment.Center);
        // Door opening
        canvas.FillColor = TealDark;
        canvas.FillRoundedRectangle(108, 158, 70, 56, 4);
        canvas.FillColor = TealSoft.WithAlpha(0.6f);
        canvas.FillRectangle(112, 166, 62, 4);
        canvas.FillRectangle(112, 176, 62, 4);
        canvas.FillRectangle(112, 186, 62, 4);
        // Side windows
        canvas.FillColor = TealSoft;
        canvas.FillRoundedRectangle(56, 160, 20, 26, 3);
        canvas.FillRoundedRectangle(212, 160, 20, 26, 3);

        // Truck: trailer + cabin heading right
        canvas.FillColor = TealMid;
        canvas.FillRoundedRectangle(170, 168, 118, 54, 6);
        canvas.FillColor = Colors.White.WithAlpha(0.25f);
        canvas.FillRectangle(178, 176, 102, 5);
        canvas.FillRectangle(178, 186, 102, 5);
        // Cabin
        canvas.FillColor = Teal;
        var cab = new PathF();
        cab.MoveTo(288, 222);
        cab.LineTo(288, 176);
        cab.LineTo(310, 176);
        cab.LineTo(326, 194);
        cab.LineTo(326, 222);
        cab.Close();
        canvas.FillPath(cab);
        // Windshield
        canvas.FillColor = TealSoft;
        var glass = new PathF();
        glass.MoveTo(294, 182);
        glass.LineTo(308, 182);
        glass.LineTo(319, 195);
        glass.LineTo(294, 195);
        glass.Close();
        canvas.FillPath(glass);
        // Wheels
        DrawWheel(canvas, 196, 224);
        DrawWheel(canvas, 224, 224);
        DrawWheel(canvas, 306, 224);

        // Gate booth
        canvas.FillColor = WallLight;
        canvas.FillRoundedRectangle(360, 170, 44, 52, 4);
        canvas.FillColor = TealMid;
        canvas.FillRoundedRectangle(354, 162, 56, 12, 4);
        canvas.FillColor = TealSoft;
        canvas.FillRoundedRectangle(370, 182, 24, 16, 3);
        // Barrier arm
        canvas.StrokeColor = TealDark;
        canvas.StrokeSize = 5;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(360, 200, 318, 214);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2;
        canvas.DrawLine(352, 203, 344, 206);
        canvas.DrawLine(338, 208, 330, 211);

        // Foliage accents
        canvas.FillColor = TealDark.WithAlpha(0.75f);
        canvas.FillEllipse(2, 226, 34, 44);
        canvas.FillEllipse(24, 238, 26, 34);
        canvas.FillColor = Teal.WithAlpha(0.55f);
        canvas.FillEllipse(14, 244, 24, 30);
        canvas.FillEllipse(414, 240, 20, 26);

        canvas.RestoreState();
    }

    private static void DrawCloud(ICanvas canvas, float x, float y, float k)
    {
        canvas.FillEllipse(x, y + 8 * k, 46 * k, 20 * k);
        canvas.FillEllipse(x + 12 * k, y, 30 * k, 22 * k);
        canvas.FillEllipse(x + 28 * k, y + 6 * k, 34 * k, 20 * k);
    }

    private static void DrawWheel(ICanvas canvas, float cx, float cy)
    {
        canvas.FillColor = Ink;
        canvas.FillCircle(cx, cy, 12);
        canvas.FillColor = Color.FromArgb("#9AA6B2");
        canvas.FillCircle(cx, cy, 5);
    }
}

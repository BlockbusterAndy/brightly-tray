using System.Drawing;
using System.Drawing.Drawing2D;

namespace MonitorBrightness.UI;

/// <summary>Draws a small sun/brightness glyph at runtime so the app doesn't need a shipped .ico asset.</summary>
internal static class TrayIconFactory
{
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var accent = Color.FromArgb(255, 255, 185, 0);
            using var corePen = new Pen(accent, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var coreBrush = new SolidBrush(accent);

            const float cx = 16f, cy = 16f, coreRadius = 6f, rayInner = 10f, rayOuter = 14f;

            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                var x1 = cx + rayInner * (float)Math.Cos(angle);
                var y1 = cy + rayInner * (float)Math.Sin(angle);
                var x2 = cx + rayOuter * (float)Math.Cos(angle);
                var y2 = cy + rayOuter * (float)Math.Sin(angle);
                g.DrawLine(corePen, x1, y1, x2, y2);
            }

            g.FillEllipse(coreBrush, cx - coreRadius, cy - coreRadius, coreRadius * 2, coreRadius * 2);
        }

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Zeiterfassung;

/// <summary>
/// Zeichnet die Tagessumme zweizeilig ("3" ueber "42") in ein Tray-Icon.
/// Laufend = Akzent-Blau, gestoppt = hellgrau. Transparenter Hintergrund.
/// Windows skaliert das 32px-Bitmap fuer die jeweilige Tray-Groesse herunter.
/// </summary>
public static class TrayIconRenderer
{
    // Akzent (Windows-Blau, nah am macOS-accentColor) / gestoppt-Grau.
    private static readonly Color Running = Color.FromArgb(255, 72, 160, 255);
    private static readonly Color Stopped = Color.FromArgb(255, 205, 205, 210);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Baut ein Icon fuer die zweizeilige Zeit. Der Aufrufer MUSS das vorherige
    /// Icon via <see cref="Dispose"/> freigeben (GetHicon leakt sonst GDI-Handles).
    /// </summary>
    public static Icon Render(TimeSpan today, bool running, int size = 32)
    {
        var total = (int)today.TotalSeconds;
        if (total < 0) total = 0;
        var topLine = (total / 3600).ToString();          // Stunden, ohne Fuehrende
        var botLine = ((total % 3600) / 60).ToString("00"); // Minuten, 2-stellig

        var color = running ? Running : Stopped;

        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            var half = size / 2f;
            DrawFitted(g, topLine, new RectangleF(0, 0, size, half), color);
            DrawFitted(g, botLine, new RectangleF(0, half, size, half), color);
        }

        var hIcon = bmp.GetHicon();
        // .NET-Icon ueber Handle bauen und sofort eine handle-unabhaengige Kopie ziehen,
        // damit wir das rohe GDI-Handle deterministisch freigeben koennen.
        using var tmp = Icon.FromHandle(hIcon);
        var icon = (Icon)tmp.Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    /// <summary>Zeichnet Text moeglichst gross in die Box (bold, zentriert).</summary>
    private static void DrawFitted(Graphics g, string text, RectangleF box, Color color)
    {
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };

        // Groesste Schrift finden, die in die Box passt (kondensiert wirkt am 16px-Ende sauberer).
        float fontSize = box.Height;
        for (; fontSize >= 4f; fontSize -= 0.5f)
        {
            using var probe = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var m = g.MeasureString(text, probe);
            if (m.Width <= box.Width && m.Height <= box.Height) break;
        }

        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(text, font, brush, box, fmt);
    }

    public static void Dispose(Icon? icon)
    {
        if (icon is null) return;
        DestroyIcon(icon.Handle);
        icon.Dispose();
    }
}

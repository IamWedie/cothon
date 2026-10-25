using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Cothon.Core.Screen;

/// <summary>
/// Captures the primary screen area with GDI and downscales toward a
/// readable size for the model. Returns null (never throws) when capture
/// is impossible, so a missed reading can never break a turn.
/// </summary>
public static class SystemScreenCapture
{
    public const int MaxUnits = 1280;

    // Virtual-desktop metrics (SM_XVIRTUALSCREEN … SM_CYVIRTUALSCREEN).
    private const int SmX = 76;
    private const int SmY = 77;
    private const int SmCx = 78;
    private const int SmCy = 79;

    public static ScreenFrame? Capture()
    {
        try
        {
            var bounds = new Rectangle(
                GetSystemMetrics(SmX), GetSystemMetrics(SmY),
                GetSystemMetrics(SmCx), GetSystemMetrics(SmCy));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            using var fullBmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(fullBmp))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);
            }

            var scale = Math.Min(1.0, (double)MaxUnits / Math.Max(bounds.Width, bounds.Height));
            var targetW = (int)(bounds.Width * scale);
            var targetH = (int)(bounds.Height * scale);

            using var scaled = new Bitmap(targetW, targetH);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(fullBmp, 0, 0, targetW, targetH);
            }

            using var ms = new MemoryStream();
            scaled.Save(ms, ImageFormat.Jpeg);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
                return null;

            return new ScreenFrame(bytes, targetW, targetH, "image/jpeg");
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

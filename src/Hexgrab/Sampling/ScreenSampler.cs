using System.Windows.Media;
using static Hexgrab.Native.NativeMethods;

namespace Hexgrab.Sampling;

/// <summary>
/// Reads the cursor position and the screen pixel under it, in physical pixels.
/// The app is Per-Monitor V2 DPI aware (see app.manifest), so both calls use the same
/// coordinate space on every monitor and scale factor.
/// </summary>
internal static class ScreenSampler
{
    public static POINT GetCursorPosition()
    {
        GetCursorPos(out var point);
        return point;
    }

    public static Color? GetPixelColor(POINT point)
    {
        var screenDc = GetDC(0);
        try
        {
            var colorRef = GetPixel(screenDc, point.X, point.Y);
            if (colorRef == CLR_INVALID)
            {
                return null;
            }

            // COLORREF is 0x00BBGGRR.
            return Color.FromRgb((byte)colorRef, (byte)(colorRef >> 8), (byte)(colorRef >> 16));
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    /// <summary>
    /// Refresh rate of the primary display. PowerToys polls at this rate, and so do we:
    /// faster than the screen can change is wasted work, slower feels laggy.
    /// </summary>
    public static int GetPrimaryRefreshRate()
    {
        var mode = new DEVMODEW { dmSize = 220 };
        if (EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmDisplayFrequency > 1)
        {
            return (int)mode.dmDisplayFrequency;
        }

        return 60;
    }

    /// <summary>Work area (screen minus taskbar) of the monitor that contains the point.</summary>
    public static RECT GetWorkArea(POINT point)
    {
        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(monitor, ref info);
        return info.rcWork;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

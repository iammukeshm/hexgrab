using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Hexgrab.Native.NativeMethods;

namespace Hexgrab.Picker;

/// <summary>
/// Small floating label (swatch + HEX) that follows the cursor.
/// It is click-through and never takes focus, so the app under the cursor keeps working normally.
/// </summary>
internal partial class OverlayWindow : Window
{
    // Distance from the cursor in physical pixels. Keeps the label off the pixel being sampled.
    private const int CursorOffset = 24;

    private nint _hwnd;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    public void ShowColor(Color color, string hex)
    {
        Swatch.Background = new SolidColorBrush(color);
        HexText.Text = hex;
    }

    public void ShowCopied(Color color, string hex)
    {
        Swatch.Background = new SolidColorBrush(color);
        HexText.Text = $"Copied {hex}";
    }

    /// <summary>
    /// Places the label below-right of the cursor, flipping to the other side near the edges of
    /// the cursor's monitor. All math is in physical pixels (SetWindowPos), so it stays correct
    /// across monitors with different scale factors.
    /// </summary>
    public void MoveNear(POINT cursor, RECT workArea)
    {
        if (_hwnd == 0)
        {
            return;
        }

        GetWindowRect(_hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = cursor.X + CursorOffset;
        var y = cursor.Y + CursorOffset;

        if (x + width > workArea.Right)
        {
            x = cursor.X - CursorOffset - width;
        }

        if (y + height > workArea.Bottom)
        {
            y = cursor.Y - CursorOffset - height;
        }

        x = Math.Max(x, workArea.Left);
        y = Math.Max(y, workArea.Top);

        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

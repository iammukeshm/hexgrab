using System.ComponentModel;
using System.Windows.Interop;
using static Hexgrab.Native.NativeMethods;

namespace Hexgrab.Input;

/// <summary>
/// System-wide hotkey via RegisterHotKey on a message-only window.
/// PowerToys gets its hotkey from the PowerToys runner; hexgrab is a single process, so it registers its own.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x4847; // "HG"
    private static readonly nint HWND_MESSAGE = -3;

    private readonly HwndSource _source;
    private readonly Action _onPressed;

    public GlobalHotkey(uint modifiers, uint virtualKey, Action onPressed)
    {
        _onPressed = onPressed;
        _source = new HwndSource(new HwndSourceParameters("hexgrab-hotkey")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);

        if (!RegisterHotKey(_source.Handle, HotkeyId, modifiers | MOD_NOREPEAT, virtualKey))
        {
            var error = new Win32Exception();
            _source.Dispose();
            throw new InvalidOperationException($"Could not register the hotkey. Another app may already use it. ({error.Message})", error);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            _onPressed();
        }

        return 0;
    }

    public void Dispose()
    {
        UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

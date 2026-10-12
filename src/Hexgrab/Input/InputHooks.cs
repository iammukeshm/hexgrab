using System.ComponentModel;
using System.Runtime.InteropServices;
using static Hexgrab.Native.NativeMethods;

namespace Hexgrab.Input;

internal enum PickerInput
{
    Pick,
    Cancel,
}

/// <summary>
/// Low-level mouse and keyboard hooks, installed only while a pick is in progress.
/// Clicks and keys used by the picker are swallowed so they never reach the app underneath.
/// </summary>
internal static unsafe class InputHooks
{
    private const uint VK_RETURN = 0x0D;
    private const uint VK_ESCAPE = 0x1B;

    private static nint _mouseHook;
    private static nint _keyboardHook;
    private static Action<PickerInput>? _onInput;

    // Each button press is swallowed down AND up. Passing a lone "up" to the app below can trigger its click handler.
    private static bool _swallowLeftUp;
    private static bool _swallowRightUp;
    private static bool _uninstallPending;

    public static bool IsInstalled => _mouseHook != 0;

    /// <summary>Installs both hooks. Must be called on a thread that pumps messages (the WPF UI thread).</summary>
    public static void Install(Action<PickerInput> onInput)
    {
        _onInput = onInput;
        _uninstallPending = false;

        if (IsInstalled)
        {
            return;
        }

        var module = GetModuleHandleW(null);

        _mouseHook = SetWindowsHookExW(WH_MOUSE_LL, &MouseProc, module, 0);
        if (_mouseHook == 0)
        {
            throw new Win32Exception();
        }

        _keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, module, 0);
        if (_keyboardHook == 0)
        {
            var error = new Win32Exception();
            Uninstall();
            throw error;
        }
    }

    /// <summary>
    /// Removes the hooks. If a swallowed button is still held down, removal waits for its release,
    /// otherwise the app underneath would receive the "up" without the "down".
    /// </summary>
    public static void Uninstall(bool force = false)
    {
        _onInput = null;

        if (!force && (_swallowLeftUp || _swallowRightUp))
        {
            _uninstallPending = true;
            return;
        }

        _uninstallPending = false;
        _swallowLeftUp = false;
        _swallowRightUp = false;

        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }

        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private static nint SwallowRelease()
    {
        if (_uninstallPending && !_swallowLeftUp && !_swallowRightUp)
        {
            // Hook callbacks run on the installing (UI) thread; unhook after this callback returns.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => Uninstall());
        }

        return 1;
    }

    [UnmanagedCallersOnly]
    private static nint MouseProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN:
                    _swallowLeftUp = true;
                    _onInput?.Invoke(PickerInput.Pick);
                    return 1;
                case WM_LBUTTONUP when _swallowLeftUp:
                    _swallowLeftUp = false;
                    return SwallowRelease();
                case WM_RBUTTONDOWN:
                    _swallowRightUp = true;
                    _onInput?.Invoke(PickerInput.Cancel);
                    return 1;
                case WM_RBUTTONUP when _swallowRightUp:
                    _swallowRightUp = false;
                    return SwallowRelease();
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static nint KeyboardProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            switch (key.vkCode)
            {
                case VK_ESCAPE:
                    _onInput?.Invoke(PickerInput.Cancel);
                    return 1;
                case VK_RETURN:
                    _onInput?.Invoke(PickerInput.Pick);
                    return 1;
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }
}

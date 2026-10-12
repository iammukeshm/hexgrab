using System.Windows.Media;
using System.Windows.Threading;
using Hexgrab.Input;
using Hexgrab.Sampling;

namespace Hexgrab.Picker;

/// <summary>
/// One pick: hotkey → follow the cursor → click/Enter copies the HEX, right-click/Esc cancels.
/// </summary>
internal sealed class PickerSession
{
    private static readonly TimeSpan CopiedFeedbackDuration = TimeSpan.FromMilliseconds(700);

    private readonly Dispatcher _dispatcher;
    private readonly OverlayWindow _overlay = new();
    private readonly DispatcherTimer _sampleTimer;
    private readonly DispatcherTimer _hideTimer;

    private Color _currentColor;
    private string _currentHex = "#000000";

    public bool IsActive { get; private set; }

    public PickerSession(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;

        // Poll at the display refresh rate rather than reacting to mouse moves.
        // Mouse-move events miss changes under a still cursor (video, animations, scrolling).
        var refreshRate = ScreenSampler.GetPrimaryRefreshRate();
        _sampleTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / refreshRate),
        };
        _sampleTimer.Tick += (_, _) => Sample();

        _hideTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = CopiedFeedbackDuration };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _overlay.Hide();
        };
    }

    public void Start()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        _hideTimer.Stop();

        // The hook callback runs inside Windows' input pipeline and must return fast,
        // so the real work is queued onto the dispatcher.
        InputHooks.Install(input => _dispatcher.BeginInvoke(() => OnInput(input)));

        _overlay.Show();
        Sample();
        _sampleTimer.Start();
    }

    private void Sample()
    {
        var cursor = ScreenSampler.GetCursorPosition();
        if (ScreenSampler.GetPixelColor(cursor) is { } color)
        {
            _currentColor = color;
            _currentHex = ScreenSampler.ToHex(color);
            _overlay.ShowColor(_currentColor, _currentHex);
        }

        _overlay.MoveNear(cursor, ScreenSampler.GetWorkArea(cursor));
    }

    private void OnInput(PickerInput input)
    {
        if (!IsActive)
        {
            return;
        }

        if (input == PickerInput.Pick)
        {
            // Sample once more so the copied value is the pixel under the cursor at click time.
            Sample();
            End();

            if (ClipboardWriter.TryCopy(_currentHex))
            {
                _overlay.ShowCopied(_currentColor, _currentHex);
                _hideTimer.Start();
                return;
            }
        }
        else
        {
            End();
        }

        _overlay.Hide();
    }

    private void End()
    {
        IsActive = false;
        _sampleTimer.Stop();
        InputHooks.Uninstall();
    }

    public void Shutdown()
    {
        _sampleTimer.Stop();
        _hideTimer.Stop();
        InputHooks.Uninstall(force: true);
        _overlay.Close();
    }
}

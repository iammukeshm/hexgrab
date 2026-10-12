using System.Windows;
using Hexgrab.Input;
using Hexgrab.Picker;
using static Hexgrab.Native.NativeMethods;
using Forms = System.Windows.Forms;

namespace Hexgrab;

public partial class App : Application
{
    // Win + Shift + H ("H" for hex). PowerToys uses Win + Shift + C, so both can run side by side.
    private const uint HotkeyModifiers = MOD_WIN | MOD_SHIFT;
    private const uint HotkeyKey = 0x48; // 'H'
    private const string HotkeyLabel = "Win + Shift + H";

    private Mutex? _singleInstance;
    private GlobalHotkey? _hotkey;
    private PickerSession? _picker;
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\hexgrab-single-instance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        _picker = new PickerSession(Dispatcher);

        try
        {
            _hotkey = new GlobalHotkey(HotkeyModifiers, HotkeyKey, _picker.Start);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "hexgrab", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _trayIcon = CreateTrayIcon();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Pick a color", null, (_, _) => _picker?.Start());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = $"hexgrab ({HotkeyLabel})",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                _picker?.Start();
            }
        };

        icon.ShowBalloonTip(3000, "hexgrab is running", $"Press {HotkeyLabel} to pick a color.", Forms.ToolTipIcon.None);
        return icon;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _picker?.Shutdown();
        _hotkey?.Dispose();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

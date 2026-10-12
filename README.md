# hexgrab

A tiny Windows color picker. Press **Win + Shift + H**, point at any pixel on screen, click, and the HEX code is in your clipboard.

Built with .NET 10 and WPF. No NuGet packages, under 750 lines of C# including comments.

## Why this exists

hexgrab is a learning project. I used [REA (Reverse Engineer Anything)](https://github.com/morluto/rea) to study how the Color Picker in [Microsoft PowerToys](https://github.com/microsoft/PowerToys) works from its **shipped binaries**, then wrote my own minimal version from those findings.

PowerToys is MIT-licensed, so studying it is allowed, and because its source is public I could check every REA finding against the real code. REA got 8 of 9 findings fully right. See [docs/rea-findings.md](docs/rea-findings.md).

> **Please don't point reverse engineering tools at commercial software you don't have the rights to study.** Most commercial licenses forbid it, and copying code from a decompiled binary is copyright infringement. Pick open source projects with permissive licenses, or your own software.

No PowerToys code (decompiled or original) is in this repo. hexgrab is an independent implementation and is not affiliated with Microsoft or PowerToys.

## Usage

| Action | Input |
|--------|-------|
| Start picking | **Win + Shift + H**, or left-click the tray icon |
| Copy HEX and stop | Left click or **Enter** |
| Cancel | Right click or **Esc** |
| Exit | Tray icon → Exit |

## Download

Grab an exe from the [latest release](https://github.com/iammukeshm/hexgrab/releases/latest). Windows 10 or 11, x64.

| File | Size | Needs |
|------|------|-------|
| `hexgrab-win-x64.exe` | ~72 MB | Nothing, the .NET runtime is bundled |
| `hexgrab-win-x64-small.exe` | ~185 KB | The [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |

The exe is not code-signed, so Windows SmartScreen will warn you on first run (**More info → Run anyway**). If you'd rather not trust a binary, build it yourself (below). `SHA256SUMS.txt` in the release has the checksums.

## Build from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows 10 or 11.

```bash
dotnet run --project src/Hexgrab/Hexgrab.csproj
```

## How it works

| Piece | Where | What it does |
|-------|-------|--------------|
| Global hotkey | `Input/GlobalHotkey.cs` | `RegisterHotKey` on a message-only window |
| Mouse + keyboard hooks | `Input/InputHooks.cs` | `WH_MOUSE_LL` / `WH_KEYBOARD_LL`, installed only while picking. Clicks and keys used by the picker are swallowed (down **and** up) so they never reach the app underneath |
| Sampling | `Sampling/ScreenSampler.cs` | `GetCursorPos` + `GetPixel` on the screen DC, polled at the display refresh rate |
| Overlay | `Picker/OverlayWindow.xaml` | Click-through, non-activating label that flips near monitor edges |
| Clipboard | `Picker/ClipboardWriter.cs` | Retries when another app holds the clipboard |
| DPI | `app.manifest` | Per-Monitor V2, so cursor and pixel coordinates are physical pixels everywhere |

Two decisions came straight from the REA investigation:

- **Poll, don't wait for mouse moves.** PowerToys samples on a timer at the monitor's refresh rate. With a still cursor over a video or animation, mouse-move events never fire, but the color under the cursor changes.
- **Retry the clipboard.** Another process can hold it open for a few milliseconds.

And one thing hexgrab deliberately does **not** copy: PowerToys swaps the system cursor scheme in the registry while picking. If the app crashes mid-pick, the user's cursors stay changed. hexgrab leaves the cursor alone.

## Tests

`tests/e2e/hexgrab-e2e.ps1` launches hexgrab, opens a solid-color test window, and drives the real hotkey, clicks and keys:

```powershell
dotnet build hexgrab.slnx
./tests/e2e/hexgrab-e2e.ps1 -ExePath src/Hexgrab/bin/Debug/net10.0-windows/hexgrab.exe
```

Keep the target spot clear of other always-on-top windows; the test stops with an error if something covers it.

It checks: click copies the HEX, the click is swallowed, Esc cancels, Enter copies, hooks are removed after a pick, and a color change under a still cursor is picked up.

## Known limitations

- Tested on a single monitor at 125% scale. Multi-monitor with mixed scale factors is designed for (physical pixels, per-monitor work area) but not yet tested on real hardware.
- The overlay is a normal topmost window, so Windows shell flyouts (Start, calendar, notification center) draw above it.
- HEX only. No RGB/HSL formats, history, magnifier or settings yet.

## License

[MIT](LICENSE)

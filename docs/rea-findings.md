# REA findings: PowerToys Color Picker 0.98.0

Investigation done 2026-10-12 with REA 6.4.0 (Ghidra 12.1.4 for native code) through Claude Code, on the PowerToys build installed from the official release. Evidence bundle for the native session: [evidence/colorpicker-native-session.json](evidence/colorpicker-native-session.json) (8 records).

## Targets

| File | Kind | SHA-256 |
|------|------|---------|
| `PowerToys.ColorPickerUI.dll` | .NET (IL-only, modern .NET, x64) | `a4989ea8...ab88b` |
| `PowerToys.ColorPicker.dll` | Native C++ module DLL (x64) | `68119b6e...d6e3` |

## Workflow that worked

1. `inspect_managed_artifact` → what is it (managed? R2R? AOT?)
2. `inspect_managed_native_boundaries` → Win32 APIs = rough architecture in one call
3. `inspect_managed_members` (CLI, 2.7 MB JSON - too big for MCP) → type names + call edges, filtered with a small Python script
4. `open_binary` (Ghidra) on the native DLL → `search_strings` → `xrefs` → `procedure_pseudo_code` → `procedure_callers` → `procedure_pseudo_code`
5. `search_procedures` (regex) to find imports - `find_xrefs_to_name("ShellExecuteExW")` **failed** because Ghidra names imports `SHELL32.DLL::ShellExecuteExW`
6. `export_evidence_bundle`

Lesson: the first string hit landed in a **logging wrapper** (fmt/spdlog), not the real logic. Going one caller up found the launcher. Good honest beat for the video.

## Findings (from binaries only)

### Native module (`PowerToys.ColorPicker.dll`, Ghidra)

- On activation it logs "Starting ColorPicker process", gets its own PID, converts it to a decimal string and launches `PowerToys.ColorPickerUI.exe` with **`ShellExecuteExW`**, PID as the argument.
- `fMask = 0x440` = `SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI` → it keeps the process handle (stored at `this+0x50`).
- On failure: logs "ColorPicker failed to start. {}" with the `GetLastError` message.
- Imports `CreateEventW`, `SetEvent`, `ResetEvent` + strings `Local\ShowColorPickerEvent-…`, `Local\TerminateColorPickerEvent-…` → controls the UI through **named events**.

### .NET UI (`PowerToys.ColorPickerUI.dll`, managed)

- **Colour sampling is polled, not event-driven.** `MouseInfoProvider` uses a `DispatcherTimer`; interval derived from the primary monitor's refresh rate (`GetMainDisplayRefreshRate` → `EnumDisplaySettingsW`). Each tick: `GetCursorPos` → `CopyFromScreen` into a tiny bitmap → `GetPixel`.
- **Low-level mouse hook** (`SetWindowsHookEx`) only for clicks/wheel: primary down = pick, middle down / secondary up = close, wheel = zoom.
- **Low-level keyboard hook** (`GlobalKeyboardHook` + `KeyboardMonitor`): Esc → close, Enter → pick, arrows → `MoveCursor` (`GetCursorPos`/`SetCursorPos`), plus activation shortcut matching.
- **Clipboard:** `SetDataObject` in a retry loop; on failure logs the window holding the clipboard (`GetOpenClipboardWindow` + `GetWindowText`) and `Sleep`s.
- **Custom cursor:** `CursorManager` writes the system cursor scheme (registry `SetValue`) and applies it with `SystemParametersInfo`, then restores the backup.
- **Multi-monitor:** `MonitorResolutionHelper` (`EnumDisplayMonitors`, `GetMonitorInfo`, DPI from the main window).
- **Magnifier:** `ZoomWindowHelper.SetZoomImage` → `CopyFromScreen` of a region, zoom factor via `Math.Pow`.

## Scorecard vs real source (microsoft/PowerToys @ v0.98.0, MIT)

| REA claim | Source | Verdict |
|-----------|--------|---------|
| Launches UI via `ShellExecuteExW` with own PID as argument | `dllmain.cpp:115-125` (`GetCurrentProcessId`, `sei.lpParameters`) | ✅ Correct |
| `fMask` = NOCLOSEPROCESS \| FLAG_NO_UI | `dllmain.cpp:121` | ✅ Correct (exact) |
| UI exe name `PowerToys.ColorPickerUI.exe` | `dllmain.cpp:122` | ✅ Correct |
| Named events signal the UI | `on_hotkey` → `SetEvent(m_hInvokeEvent)`; disable → `SetEvent(m_hAppTerminateEvent)` | ✅ Correct |
| "Native module owns the hotkey" | Module declares hotkeys (`get_hotkeys`); the **PowerToys runner** registers them and calls `on_hotkey` | ⚠️ Partly - the module handles it, the runner registers it |
| Polling timer at monitor refresh rate | `MouseInfoProvider.cs:35-36` `1000.0 / GetMainDisplayRefreshRate()` | ✅ Correct |
| Pixel via `CopyFromScreen` + `GetPixel` | `MouseInfoProvider.cs:104-114` | ✅ Correct |
| Clipboard retry loop with sleep | `ClipboardHelper.cs:27-51` - 10 tries, 10 ms | ✅ Correct (counts not recovered from call edges alone) |
| Cursor swap via registry + `SystemParametersInfo` | `CursorManager.cs` - `HKCU\Control Panel\Cursors`, `SPI_SETCURSORS` | ✅ Correct |

**8 of 9 fully correct, 1 partly correct.** The miss is a classic static-analysis blind spot: the hotkey registration lives in a different binary (the runner) that we didn't analyze.

No decompiled or original PowerToys code is copied into hexgrab. The public source was only used to grade REA.

## What this means for hexgrab

| PowerToys does | hexgrab does | Why |
|----------------|--------------|-----|
| Runner + native module + separate UI process + named events | One .NET process owns hotkey and UI | We don't need a plugin host |
| Hotkey registered by the runner | `RegisterHotKey` directly | Single process |
| Timer polling at refresh rate + `CopyFromScreen`/`GetPixel` | Same idea (timer at refresh rate), our own implementation | Smooth and cheap; mouse-move events alone miss screen changes under a still cursor |
| Mouse hook for clicks | Same | Click must not reach the app underneath |
| Keyboard hook for Esc/Enter/arrows | Same (Esc + Enter in MVP, arrows stretch) | |
| Clipboard retry loop | Same pattern | Clipboard is a shared, lockable resource |
| Changes the **global** cursor via registry | **Skip** - use an overlay window cursor instead | Writing the user's cursor scheme is risky if the app crashes mid-pick |
| Per-monitor DPI handling | Same requirement (MVP #5) | |

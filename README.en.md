# WindowMark (窗签)

Sticky notes for your Windows title bars — tell look-alike windows apart at a glance.

[简体中文](README.md) | English

![WindowMark adds custom labels to windows](docs/screenshot.png)

When several chat windows, editors, or terminals are open at once, they often look nearly identical, and switching to the right one becomes a guessing game. WindowMark lets you add a custom label on top of any window. The label is **display-only**: the window's real title, taskbar entry, and Alt+Tab entry are left untouched.

## Features

- **Overlay labels**: a colored chip is drawn over the title bar without modifying the window itself.
- **Hotkey**: press `Ctrl + Alt + L` to label the window under the mouse pointer.
- **Tray menu**: alternatively, right-click the tray icon and pick a window from "Select a window to label".
- **Custom look**: up to 24 characters, 8 built-in colors plus a custom color picker, full Unicode support.
- **Free layout**: choose a 3×3 position, one of four sizes, or restore defaults directly in the create/edit dialog. Text, color, position, and size are previewed live, and canceling restores the original state. After saving, enter adjust mode from the tray menu to drag the label or resize it from the bottom-right grip.
- **Click-through**: labels normally never intercept mouse input, take focus, or appear in the taskbar or Alt+Tab; they are interactive only in adjust mode.
- **Tracks the window**: labels follow moves and resizes using their saved anchor, hide when the window is minimized or moved to another virtual desktop, and are cleaned up when the window closes.
- **Persistent rules**: rules are stored by executable path + window class + native title, and labels are restored after restarting WindowMark or reopening a matching window.
- **DPI aware**: Per-Monitor V2, verified at 150% scaling.
- **Local-first**: rules live in a local JSON file. No network requests, no telemetry.

> WindowMark is at an early `0.2.0` stage. The user interface is currently in Simplified Chinese.

## Download

Get `WindowMark-<version>-win-x64.exe` from [Releases](https://github.com/jinksw/windowmark/releases/latest) and run it — no .NET installation required. The release is a self-contained single file of about 63 MB.

The executable is not code-signed, so Windows SmartScreen may show "Windows protected your PC" on first launch. Click "More info → Run anyway".

## Requirements

- Windows 10 / Windows 11, x64 (tested on Windows 11 at 150% scaling)
- To build: .NET 8 SDK

## Build and run

```powershell
dotnet build WindowMark.csproj -c Release
dotnet run --project WindowMark.csproj
```

Or launch the built executable directly:

```powershell
.\bin\Release\net8.0-windows\WindowMark.exe
```

Create the self-contained single-file build used by Releases (the target machine needs no .NET installation):

```powershell
dotnet publish WindowMark.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Pushing a `v*` tag makes the [release workflow](.github/workflows/release.yml) build and publish a GitHub Release automatically.

## Usage

1. Start WindowMark. A `W` icon appears in the notification area; no main window is shown.
2. Hover the target window and press `Ctrl + Alt + L`.
3. Enter a label, pick a color (the last swatch opens a custom color picker), choose a preset under "位置" and "大小", and click "Save".
4. Press the hotkey again on the same window to edit or delete its label.
5. For fine-tuning after saving, right-click the tray icon and choose "调整标签位置与大小". Labels become interactive:
   - click a label to select it, then drag its body to move it;
   - drag the bottom-right grip to resize it;
   - use the toolbar for 3×3 position presets, four size presets, or "Restore defaults";
   - click "完成", or toggle the tray item again, to leave adjust mode. Click-through is restored afterwards.

   Position and size are saved with the rule; all windows matched by the same rule share the same layout.
6. If the hotkey is already taken by another app, right-click the tray icon and use "Select a window to label".
7. To quit, right-click the tray icon and choose "Exit".

Labels hide automatically when their window is minimized and reappear when it is restored.

## Data and privacy

| File | Content |
| --- | --- |
| `%APPDATA%\WindowMark\labels.json` | Label rules |
| `%APPDATA%\WindowMark\log.txt` | Diagnostic log |

Everything stays on the local machine; the app makes no network requests. You can open the data folder from the tray menu.

## Known limitations

- The default position assumes a standard title bar. Apps with immersive custom title bars (some Electron / WinUI apps and games) may still need the label to be dragged manually. Positions are stored as a 3×3 anchor plus offset, so labels stay attached to the corresponding edge when the window is resized.
- Rule matching includes the native window title. Windows whose titles change frequently, such as browser tabs, editors showing a file name, or terminals showing the current directory, may not match after being reopened and will need to be labeled again.
- Windows running elevated may not show the label due to Windows UIPI restrictions.
- Only regular top-level windows are supported; the desktop, taskbar, and tool windows are ignored.
- There is no installer, auto-update, or run-at-startup support yet.

## How it works

WindowMark is a .NET 8 WPF app with no main window: it consists of a tray icon, a label editor, overlay windows, and an adjust toolbar.

- Top-level windows are enumerated through Win32, and the real frame is read with DWM's `DWMWA_EXTENDED_FRAME_BOUNDS`.
- Each label is a borderless window with `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, so it is click-through and sits directly above its target.
- Adjust mode temporarily removes `WS_EX_TRANSPARENT` so labels can receive mouse drags and resizes; click-through is restored on exit.
- Positions are stored as a 3×3 anchor plus a DIP offset, and size as a scale factor, so labels stay attached to the relevant edge when windows resize.
- Label positions are synchronized at roughly 30 FPS, while a one-second maintenance pass handles new, closed, and minimized windows.
- Rules are stored in `%APPDATA%\WindowMark\labels.json`.
- The app declares Per-Monitor V2 DPI awareness and uses a single-instance mutex to prevent duplicate launches.

### Project layout

```text
WindowMark/
├─ Core/
│  ├─ AppController.cs      # Startup, hotkey, and labeling flow
│  ├─ OverlayManager.cs     # Overlay lifetime and window tracking
│  ├─ WindowService.cs      # Window enumeration and filtering
│  ├─ LabelStore.cs         # Rule persistence
│  ├─ TrayController.cs     # Tray icon and menu
│  └─ HotkeyWindow.cs       # Global hotkey
├─ UI/
│  ├─ OverlayWindow.xaml    # Label overlay
│  ├─ LabelEditorWindow.xaml# Label editor
│  ├─ LayoutToolbarWindow.xaml # Position and size toolbar
│  └─ Palette.cs            # Colors and contrast handling
├─ Native/NativeMethods.cs  # Win32 / DWM P/Invoke
├─ app.manifest
└─ WindowMark.csproj
```

## Contributing

Issues and pull requests are welcome. Please make sure `dotnet build WindowMark.csproj -c Release` succeeds. For larger changes, open an issue first to discuss the approach.

## License

Released under the [MIT License](LICENSE).

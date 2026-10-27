# Game Music Flyout

A small always-on-top Windows flyout for seeing and controlling music while gaming. It works with any app that publishes to Windows media controls (Spotify, Chrome, Brave, Edge, Firefox, VLC, Media Player, and others).

- Title and artist. When there's no artist, the source app is shown instead (e.g. "Chrome").
- Play/pause, previous, next, and repeat (off → track → list).
- Volume of the playing app (its Windows Volume Mixer level), YouTube-style: hover, click to mute, scroll to adjust.
- A seek bar with elapsed and total time. Click or drag to seek.
- A global shortcut shows or hides the flyout without leaving your game.
- A tray icon, plus a remembered position and size.

It only reads what players report to Windows and sends commands back the same way, and sets the playing app's Windows volume. It makes no network connections, doesn't capture audio, and doesn't inject anything into games. See `ARCHITECTURE.md` for the full specification.

## Requirements

- Windows 10 version 2004 (build 19041) or newer, or Windows 11, 64-bit.
- The published `.exe` is self-contained: **no .NET installation needed**.

## Install and run

1. Build it (see below) or take `GameMusicFlyout.exe` from the publish folder.
2. Copy it anywhere permanent, e.g. `C:\Users\<you>\Apps\GameMusicFlyout\`. It's a single file.
3. Double-click it. A blue equalizer icon appears in the notification area (tray).

**Start with Windows (optional):** press `Win+R`, type `shell:startup`, and put a shortcut to the `.exe` in that folder.

**Uninstall:** exit the app, delete the `.exe`, and delete `%APPDATA%\GameMusicFlyout`.

## Using it

| Action | How |
|---|---|
| Show / hide the flyout | The shortcut (default **Ctrl+Alt+M**), or left-click the tray icon |
| Exit the app | **✕** on the flyout, or tray → **Exit** |
| Move the flyout | Drag an empty area of the card |
| Resize (75%–150%) | Hold **Ctrl** and scroll over the card, or tray → **Size** |
| App volume | Hover the speaker (bottom-left) to show the slider; click it to mute/unmute; scroll over it for ±5% |
| Change the shortcut | Tray → **Shortcut…**, press the new keys, **Save** |
| Show at startup on/off | Tray → **Show flyout when the app starts** |

- The flyout **never takes keyboard focus**. Clicking its buttons leaves your game in control of the keyboard.
- A shortcut must include **Ctrl, Alt, or Win**, so plain keys keep working in games and chat. If another app already uses the combination, you get a message and your previous shortcut stays active.
- Buttons are greyed out when the current player doesn't support them. Hover over repeat to see why.
- If a player rejects a command, a short orange message appears. That button stays disabled until the next track.

### Settings

Settings are stored in `%APPDATA%\GameMusicFlyout\settings.json`:

```json
{
  "Left": 1784,
  "Top": 1189,
  "Scale": 1.05,
  "Hotkey": "Alt+Oem3",
  "ShowOnStartup": true
}
```

You can edit this file while the app is not running. A broken or missing file falls back to defaults. Unexpected errors are logged locally to `%APPDATA%\GameMusicFlyout\error.log`.

## Known limitations

These come from Windows or the players, not from bugs in the flyout:

- **Exclusive fullscreen games hide the flyout.** Use **borderless windowed** mode. The shortcut still works in any mode, and some games may still cover ordinary windows.
- **YouTube in a browser ends its media session when a video finishes.** After that, nothing can be controlled ("Nothing playing"), so play can't restart it. Use YouTube's **Loop** (right-click the video) or a playlist/autoplay.
- **Browsers report the browser, not the website.** With no artist you'll see "Chrome", not "YouTube · Chrome".
- **Incognito/private windows** may show a placeholder such as "A site is playing media".
- **Repeat, seeking, and previous/next depend on the player.** Browsers don't support repeat, and previous/next only work when the site enables them.
- **Volume is the app's Windows volume (Volume Mixer), not the player's own slider.** For a browser it affects the whole browser, not just one tab, and it multiplies with the in-page slider (YouTube at 50% × flyout 100% = 50%). It becomes available once the app actually plays sound. Your game's volume isn't touched.
- **With several players open**, the flyout shows the session Windows considers current, usually the one playing most recently.

## Building from source

Requires the **.NET 10 SDK** on Windows (`winget install Microsoft.DotNet.SDK.10`).

### On Windows directly

```bat
dotnet build src\GameMusicFlyout\GameMusicFlyout.csproj -c Debug
dotnet publish src\GameMusicFlyout\GameMusicFlyout.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=false -p:DebugType=none -o publish
```

### From WSL (how this repo is developed)

The source of truth stays in WSL. `build.sh` mirrors it to a disposable Windows folder (`C:\Users\pcpc\source\GameMusicFlyout-build`, set in the script) and builds there, because Windows tools don't work from `\\wsl.localhost\...` paths.

```bash
./build.sh            # Debug build
./build.sh run        # Debug build, then launch on Windows
./build.sh publish    # Release single-file .exe -> <mirror>\publish\GameMusicFlyout.exe
```

Exit the app first. A running `.exe` is locked, and the script refuses to build while it's running. Never edit files in the mirror; they're overwritten on every build.

Compression is off on purpose. A compressed single file is about 78 MB instead of about 190 MB, but it uses more than twice the RAM (149 MB vs 68 MB private), because compressed code is unpacked into memory.

### Project layout

```text
src/GameMusicFlyout/
  App.xaml(.cs)                 startup/shutdown wiring, single instance, error log
  MainWindow.xaml(.cs)          the flyout card, seek-bar gestures, scaling
  HotkeyDialog.xaml(.cs)        shortcut capture dialog
  ViewModels/FlyoutViewModel.cs display state, commands, progress interpolation
  Models/                       snapshots, Hotkey, AppSettings
  Services/
    GsmTCMediaSessionService.cs Windows media sessions (GSMTC): events + commands
    SourceAppNames.cs           app ID -> friendly name ("chrome.exe" -> "Chrome") and process names
    AppVolumeService.cs         per-app Windows volume/mute via Core Audio (NAudio.Wasapi)
    GlobalHotkeyService.cs      RegisterHotKey on a message-only window
    OverlayWindowService.cs     no-activate topmost window, position, scale, on-screen clamp
    TrayIcon.cs                 notification-area icon and menu
    SettingsStore.cs            settings.json load/save
  Infrastructure/               ObservableObject, AsyncCommand, Win32 interop
  Assets/app.ico                generated by tools/make_icon.py
```

## Manual test checklist

Taken from the spec's acceptance criteria. Run it after changes, since none of it can be verified from WSL.

- [ ] With Spotify (or another player) playing, the flyout shows the title and artist.
- [ ] With a browser video that has no artist, it shows the title and the browser name.
- [ ] Play/pause, previous, and next control the player. The play/pause icon follows the player when you pause *inside* the player.
- [ ] Repeat cycles off → track → list where supported, and is greyed out with a tooltip in browsers.
- [ ] Clicking and dragging the seek bar jumps once, to where you release. Elapsed time counts up smoothly while playing.
- [ ] Closing all players shows "Nothing playing" without errors. Starting one again picks it up.
- [ ] The shortcut toggles the flyout over a **borderless windowed** game, and the game keeps keyboard input.
- [ ] The volume slider changes the player's level in the Windows Volume Mixer (and follows changes made there); mute works; the game's volume is unaffected.
- [ ] Ctrl+scroll and tray → Size change the scale. Position and size are restored after a restart.
- [ ] Changing the shortcut to one that's already taken shows an error and keeps the old one.
- [ ] Starting a second copy shows "already running".
- [ ] After **✕** or tray → Exit, the tray icon disappears and the process is gone (Task Manager).
- [ ] No network access (e.g. check with Resource Monitor → Network) and no audio capture.

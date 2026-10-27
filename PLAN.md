# Game Music Flyout — Implementation Plan & Progress

Spec: `ARCHITECTURE.md` (source of truth for requirements). This file tracks **how** we build it and **where we are**.

## Resuming after an interrupted session

1. Read `ARCHITECTURE.md`, then this file.
2. Find the first unchecked `[ ]` item below — that's where to continue. Check the "Session log" for notes on half-finished work.
3. Run `./build.sh` to confirm the current state compiles before changing anything.
4. Update the checkboxes and session log **as each item is finished**, not at the end.
5. `git log --oneline` shows completed milestones; `git status` / `git diff` shows uncommitted half-finished work.

## Environment (verified 2026-10-27)

- Dev environment: WSL (Ubuntu) on Windows 10 22H2, build 19045 (GSMTC requires ≥ 19041 ✔).
- .NET SDK 10.0.403 x64 installed on **Windows** at `C:\Program Files\dotnet\dotnet.exe`.
  - In WSL call it as `"/mnt/c/Program Files/dotnet/dotnet.exe"` (plain `dotnet` may not be on the WSL PATH until WSL restarts).
- Windows user profile: `C:\Users\pcpc`.

## Key decisions

- **Canonical source:** `/home/daki/music-sw` (WSL). Never edit the Windows copy.
- **Build mirror (disposable):** `C:\Users\pcpc\source\GameMusicFlyout-build` (`/mnt/c/Users/pcpc/source/GameMusicFlyout-build`). Synced by `build.sh`, which excludes `.git`, `bin`, `obj` and deletes stale files only inside that folder.
- Always run Windows tools with a `C:\` working directory, never `\\wsl.localhost\...`.
- **Target framework:** `net10.0-windows10.0.19041.0` (needed for GSMTC WinRT projections), `UseWPF=true`, `UseWindowsForms=true` (only for tray `NotifyIcon`).
- **Default hotkey:** `Ctrl+Alt+M` (configurable).
- **Settings file:** `%APPDATA%\GameMusicFlyout\settings.json` (window position, scale, hotkey, start-visible).
- **Focus:** the flyout window uses `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` so showing it doesn't steal keyboard focus from the game.
- No network, no audio capture, no game injection (see spec).
- **Git:** local repo on `main`, no remote. Commit after each step (or meaningful sub-step) once it builds clean.

## Steps

### 1. Solution scaffold + static mockup
- [x] `GameMusicFlyout.sln` + `src/GameMusicFlyout/GameMusicFlyout.csproj` with the target above
- [x] Folder layout from spec (`ViewModels/`, `Infrastructure/` now; `Models/`, `Services/` are created with their files in steps 2–4)
- [x] `ObservableObject`, `AsyncCommand`
- [x] Static flyout card (~380 px): title, artist/source, elapsed/total + seek bar, prev / play-pause (largest) / next / repeat, hide button (sample data in `FlyoutViewModel`; hide button exits the app until step 4 adds the tray)
- [x] `build.sh` sync-and-build script (`./build.sh` | `./build.sh run` | `./build.sh publish`)
- [x] Builds clean on Windows (0 warnings, 0 errors)

### 2. GSMTC media session service
- [x] `TrackSnapshot`, `PlaybackSnapshot` models (+ `MediaSessionState` wrapper)
- [x] `IMediaSessionService` + `GsmTCMediaSessionService`: manager init, session-list / current-session events, per-session property/playback/timeline events, unsubscribe on switch/shutdown
- [x] Metadata normalization + artist-empty fallback to source (`Services/SourceAppNames.cs`; GSMTC doesn't expose the website, so browsers show e.g. "Chrome", not "YouTube · Chrome")
- [x] Idle state when no session; all WinRT calls wrapped (no crashes)
- [x] ViewModel bound to real data, updates on dispatcher
- [x] Builds clean

### 3. Controls
- [x] Play/pause, previous, next via `Try...Async`; enabled only per `PlaybackControls`; icon follows real status
- [x] Repeat: cycle off → track → list via `TryChangeAutoRepeatModeAsync`; disabled + tooltip when unsupported
- [x] Fix (seen in user test; slider is now two-way bound to `SeekValue`, gestures handled in `MainWindow.xaml.cs`): clicking the slider set a local Value, which broke its OneWay binding, so the thumb moved without seeking and stopped following updates. Drive the slider from code/VM during drag instead of relying on OneWay binding.
- [x] Seek bar: local 250 ms interpolation timer, no thumb updates while dragging, single seek on release (100-ns ticks, clamped); disabled for no/invalid duration or unsupported; live = elapsed only
- [x] Failures are visible (brief status text) but non-fatal
- [x] Builds clean
- Notes: interpolation anchors on the source's `LastUpdatedTime` (sanitized) and re-anchors locally on play/pause without a timeline update. A failed control is disabled until the track or source changes. Press-release moves under 0.5 s don't seek.

### 4. Window behavior
- [x] Topmost, borderless, no-activate, drag-to-move, position persisted + clamped to visible screens
- [x] `IGlobalHotkeyService` + `GlobalHotkeyService` (`RegisterHotKey` via `HwndSource`); conflict surfaced to user; unregister on exit
- [x] `IOverlayWindowService` + `OverlayWindowService` (show/hide/toggle)
- [x] Scale (user request): whole card scales proportionally via `LayoutTransform`, 75%–150% in 5% steps (exact range is a choice, adjust if needed); Ctrl+mouse wheel over the card + tray "Size" submenu (e.g. 75/100/125/150%); persisted in settings; window re-clamped to screen after scaling
- [x] Tray icon: Show/Hide, Size, Exit; ✕ on the card **exits** the app (user preference; hide = shortcut or tray)
- [x] Clean shutdown: hotkey, timers, session events, tray icon released
- [x] Builds clean
- Notes: hotkey uses a message-only `HwndSource` (works while hidden); a failed change keeps the old shortcut. Change it via tray → Shortcut… (`HotkeyDialog`); a valid shortcut needs Ctrl/Alt/Win. Tray is WinForms `NotifyIcon` (`Services/TrayIcon.cs`); icon generated by `tools/make_icon.py`. Single-instance mutex. Unhandled UI exceptions are logged to `%APPDATA%\GameMusicFlyout\error.log` and the app keeps running. `EnsureOnScreen` works in device pixels against the nearest monitor's work area.

### 5. Publish + docs
- [ ] `dotnet publish -c Release -r win-x64 --self-contained` (via script)
- [ ] `README.md`: build/run instructions, hotkey, settings, limitations
- [ ] Manual test checklist for the user (from spec acceptance checklist)

## Cannot be verified from WSL (user must test on Windows)

Runtime window behavior, global hotkey, overlay over a borderless game, real Spotify / browser GSMTC sessions, seek/repeat support per source.

## Session log

- 2026-10-27: Read spec, verified environment, wrote this plan. Initialized git.
- 2026-10-27: Step 1 done. Builds clean. Visual check of the mockup not done by Claude (a screen-capture script was blocked by permissions); user to run `./build.sh run` and look. Next: step 2.
- 2026-10-27: User confirmed the mockup looks good. Asked for resizing; chose proportional **scale** (not free edge-resize). Added to step 4.
- 2026-10-27: Step 2 done. Builds clean. Runtime probe on Windows confirmed GSMTC manager works (0 sessions at the time, so metadata display not yet verified with a real player). Transport buttons are still no-ops (step 3). Next: step 3.
- 2026-10-27: User tested step 2 with Chrome: session detected, duration 2:49, and controls gated correctly (prev/next/repeat disabled). The title showed Chrome's placeholder "A site is playing media" (likely Incognito). Elapsed was stuck at 0:00 because Chrome only reports position on events; step 3's interpolation fixes that.
- 2026-10-27: User tested with Brave and Chrome both open; the flyout follows whichever is playing. No session-selection change needed. A probe showed both browsers publish sessions (`chrome.exe`, `brave.exe`).
- 2026-10-27: Step 3 done. Builds clean. Not runtime-tested by Claude (testing would control the user's real playback). User should test buttons, repeat, and seek with Chrome/Brave/Spotify. Next: step 4.
- 2026-10-27: User confirmed step 3 works. Found that YouTube in a browser **ends its GSMTC session when a video finishes** (probe: 0 sessions), so replay-at-end is impossible via GSMTC; workarounds are YouTube Loop or autoplay/playlist. Fixed the seek bar rendering fully filled when there is no duration (`SeekMaximum` is never 0).
- 2026-10-27: Step 4 done. Builds clean. Launched on Windows: the process starts with no error.log. Not verified by Claude: the hotkey, no-activate with a game, Ctrl+wheel scaling, the tray menu, and position persistence (user to test). Next: step 5.
- 2026-10-27: User tested step 4: all good. Ctrl+Alt+M was taken by another app on their PC; conflict notification worked and they picked another shortcut via tray. Default left as Ctrl+Alt+M.
- 2026-10-27: User set their shortcut to Alt+~ (stored as `Alt+Oem3`). Per the user, ✕ now exits the app instead of hiding. Pending, offered for step 5: show OEM keys by their real character (e.g. "Alt+~") and write settings JSON without escaping "+".

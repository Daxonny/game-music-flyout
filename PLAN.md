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
- **Settings file:** `%APPDATA%\GameMusicFlyout\settings.json` (window position, hotkey, start-visible).
- **Focus:** the flyout window uses `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` so showing it doesn't steal keyboard focus from the game.
- No network, no audio capture, no game injection (see spec).
- **Git:** local repo on `main`, no remote. Commit after each step (or meaningful sub-step) once it builds clean.

## Steps

### 1. Solution scaffold + static mockup
- [ ] `GameMusicFlyout.sln` + `src/GameMusicFlyout/GameMusicFlyout.csproj` with the target above
- [ ] Folder layout from spec (`ViewModels/`, `Models/`, `Services/`, `Infrastructure/`)
- [ ] `ObservableObject`, `AsyncCommand`
- [ ] Static flyout card (~380 px): title, artist/source, elapsed/total + seek bar, prev / play-pause (largest) / next / repeat, hide button
- [ ] `build.sh` sync-and-build script
- [ ] Builds clean on Windows

### 2. GSMTC media session service
- [ ] `TrackSnapshot`, `PlaybackSnapshot` models
- [ ] `IMediaSessionService` + `GsmTCMediaSessionService`: manager init, session-list / current-session events, per-session property/playback/timeline events, unsubscribe on switch/shutdown
- [ ] Metadata normalization + artist-empty fallback to source (`YouTube · Chrome`-style friendly app-ID mapping)
- [ ] Idle state when no session; all WinRT calls wrapped (no crashes)
- [ ] ViewModel bound to real data, updates on dispatcher
- [ ] Builds clean

### 3. Controls
- [ ] Play/pause, previous, next via `Try...Async`; enabled only per `PlaybackControls`; icon follows real status
- [ ] Repeat: cycle off → track → list via `TryChangeAutoRepeatModeAsync`; disabled + tooltip when unsupported
- [ ] Seek bar: local 250 ms interpolation timer, no thumb updates while dragging, single seek on release (100-ns ticks, clamped); disabled for no/invalid duration or unsupported; live = elapsed only
- [ ] Failures are visible (brief status text) but non-fatal
- [ ] Builds clean

### 4. Window behavior
- [ ] Topmost, borderless, no-activate, drag-to-move, position persisted + clamped to visible screens
- [ ] `IGlobalHotkeyService` + `GlobalHotkeyService` (`RegisterHotKey` via `HwndSource`); conflict surfaced to user; unregister on exit
- [ ] `IOverlayWindowService` + `OverlayWindowService` (show/hide/toggle)
- [ ] Tray icon: Show/Hide, Exit; hide button on card
- [ ] Clean shutdown: hotkey, timers, session events, tray icon released
- [ ] Builds clean

### 5. Publish + docs
- [ ] `dotnet publish -c Release -r win-x64 --self-contained` (via script)
- [ ] `README.md`: build/run instructions, hotkey, settings, limitations
- [ ] Manual test checklist for the user (from spec acceptance checklist)

## Cannot be verified from WSL (user must test on Windows)

Runtime window behavior, global hotkey, overlay over a borderless game, real Spotify / browser GSMTC sessions, seek/repeat support per source.

## Session log

- 2026-10-27: Read spec, verified environment, wrote this plan. Initialized git. Implementation not started.

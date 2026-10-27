# Game Music Flyout — Architecture and Build Brief

## Goal

Build a small native Windows flyout that lets one person see and control music while gaming. The important features are:

- Show the current title and artist. When the source does not report an artist (common for video), show the media title and identify the source app or browser (for example, YouTube in Chrome).
- Play/pause, previous, next, and repeat.
- A seek bar that displays elapsed and total time and lets the user click or drag to seek.
- A compact always-on-top flyout that can be shown over a borderless-windowed game, plus a global hotkey to show/hide it.

This is a local utility. It does not play audio, download media, sign into music services, or need a cloud backend.

## Recommended technology

- **Language/runtime:** C# on .NET 10 LTS, Windows-only target (`net10.0-windows`).
- **UI:** WPF. It is a mature desktop framework with straightforward window positioning, transparent/background styling, input handling, and reliable support for a small utility window. Keep the UI native; do not use Electron or a webview.
- **Media integration:** Windows Runtime **GlobalSystemMediaTransportControlsSessionManager (GSMTC)** APIs via the Windows SDK / C# WinRT projections. This is the system-supported way for a desktop app to inspect and control media sessions published by other Windows apps.
- **Window behavior:** WPF window with `Topmost=true`, no standard title bar, compact dimensions, configurable screen corner, and optional click-through only when hidden (never while the user is interacting with the flyout).
- **Architecture pattern:** small MVVM app. Keep GSMTC, hotkey registration, and native window interop behind service interfaces so they can be tested or replaced independently.
- **Packaging:** begin with a self-contained, x64 Windows publish so the user can run it without installing .NET. Do not add an installer or auto-updater in the MVP.

WPF is chosen over WinUI 3 here because this is a small, always-on-top desktop utility whose key requirements are window behavior and system interop; WinUI 3 is viable, but adds no important capability for this first version. The Windows App SDK is Microsoft's recommended platform for new Windows desktop apps, and it can also be used with WPF if a later feature needs it.

## Proposed solution layout

```text
GameMusicFlyout.sln
src/GameMusicFlyout/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  ViewModels/FlyoutViewModel.cs
  Models/TrackSnapshot.cs
  Models/PlaybackSnapshot.cs
  Services/IMediaSessionService.cs
  Services/GsmTCMediaSessionService.cs
  Services/IGlobalHotkeyService.cs
  Services/GlobalHotkeyService.cs
  Services/IOverlayWindowService.cs
  Services/OverlayWindowService.cs
  Infrastructure/AsyncCommand.cs
  Infrastructure/ObservableObject.cs
```

Avoid overengineering: no database, backend, plugin framework, audio capture, visualizer, or custom browser extension in the first release.

## Media session behavior

1. On startup, request the GSMTC session manager and subscribe to session-list and current-session changes.
2. Use the manager's current session as the initial target. If there is no session, show a clear idle state. If multiple sessions are active, the MVP may use the current system session; keep the service model ready for a future source picker.
3. Read media properties (title, artist, album title if present), source app user model ID, playback status, timeline properties, and enabled playback controls.
4. Subscribe to media-property, playback-info, and timeline change events. Update the view model on the WPF dispatcher and unsubscribe when switching sessions or shutting down.
5. Normalize empty or whitespace metadata. Display title prominently; show artist beneath it when available. When artist is empty, show the source app/browser name as context, such as `YouTube · Chrome` if a friendly mapping is available, otherwise a readable app name or ID. Do not infer a track artist from a video title.
6. Call the relevant GSMTC `Try...Async` method for playback commands. Reflect each session's advertised control support in the UI; disable a control when the active source says it is unsupported or a request fails.

The Windows media session API can only show what a player publishes. Spotify and many browsers publish metadata through Windows media controls, but a website/browser combination may omit artist, duration, seeking, or repeat support. The app must degrade gracefully and must not scrape webpages or automate browser UI as a fallback.

### Repeat semantics

Use GSMTC repeat support rather than pretending a generic repeat command is always available:

- Read `PlaybackInfo.AutoRepeatMode` and `PlaybackControls.IsRepeatEnabled`.
- When repeat is enabled, cycle the supported `MediaPlaybackAutoRepeatMode` values (off, track, list) through `TryChangeAutoRepeatModeAsync` and reflect the resulting state visually.
- If the session does not advertise repeat support, disable the repeat button and show a short tooltip such as “Repeat isn’t supported by this player.” Never emulate repeat by skipping tracks or restarting playback.

### Seek bar semantics

- Use the session timeline's position and end time to render the progress bar and elapsed / total time.
- Update the displayed position smoothly on a modest timer (around 4 updates per second); do not repeatedly call the OS API just to animate the bar.
- While the user is dragging, pause position updates to the thumb. On pointer release, convert the chosen offset to the API's 100-nanosecond ticks and call `TryChangePlaybackPositionAsync`.
- Disable seeking if duration is missing/invalid, the session reports seeking unsupported, or seeking fails. For live streams with no meaningful duration, show elapsed time if available and no seek thumb.
- Clamp the target position to the valid timeline range. Do not issue a seek for every pointer-move event.

## Flyout UX

- Compact card, roughly 360–420 px wide, with title, artist/source, elapsed/total time and seek bar, then previous / play-pause / next / repeat controls.
- Play/pause is the largest control. The state icon reflects the session's actual playback status, not just the last button click.
- The overlay starts hidden or follows an explicit user setting. A configurable global hotkey toggles it; choose a safe default and allow the user to change it. Register/unregister the hotkey cleanly and surface conflicts instead of silently failing.
- The window remains topmost while shown, can be dragged to reposition, and remembers its position. Provide a close/hide affordance and a tray/menu option to reopen it.
- Use normal desktop window layering only. Do not inject into games, hook graphics APIs, bypass anti-cheat, or claim to overlay exclusive-fullscreen games. Borderless-windowed games are the supported target; some games may still suppress ordinary windows.
- Optional auto-hide after a configurable idle timeout can be added if it stays simple; do not make it a blocker for transport controls.
- Keyboard focus should return to the game after a hotkey toggle where practical, and game input should not be swallowed while the flyout is hidden.

## Reliability and privacy

- No network calls, analytics, accounts, API keys, or telemetry.
- Do not capture or process audio. Only read metadata/timeline and send playback commands through Windows media controls.
- Handle session disappearance, app shutdown, unavailable metadata, unsupported commands, COM/WinRT exceptions, and hotkey registration failure without crashing.
- Avoid polling metadata continuously. Prefer GSMTC events and use a low-frequency UI timer only for progress display.
- Ensure event subscriptions and timers are released on shutdown; use async operations without blocking the UI thread.

## Implementation sequence

1. Create the WPF solution and a static flyout mockup with the requested controls.
2. Implement the GSMTC service and show metadata/status for the current session.
3. Wire play/pause, previous, next, repeat, and seek; gate controls by capability and verify failures are visible but non-fatal.
4. Add topmost positioning, drag-to-move, persisted location, tray access, and global show/hide hotkey.
5. Publish an x64 self-contained build and write short setup/use notes.

## Acceptance checklist

- With a supported player active, the flyout displays title and artist; for a video with no artist it displays the title and identifies the source app when possible.
- Play/pause, previous, and next control the selected/current Windows media session and the UI follows the actual session state.
- Repeat cycles only when the source advertises it; unsupported repeat is visibly disabled.
- A click/drag seek changes position for seek-capable media, with a responsive progress display and no seek spam while dragging.
- Missing sessions and unsupported metadata/controls produce a usable idle/limited state, not an exception or frozen window.
- The hotkey toggles the compact topmost window while a borderless-windowed game is running. No guarantee is made for exclusive fullscreen.
- Closing or exiting unregisters the hotkey and releases timers and session event handlers.
- The app performs no network access and does not capture audio.

## Notes for the implementer

Build and run this on Windows. A Linux/WSL environment cannot validate WPF window behavior, Windows global hotkeys, or GSMTC interoperability. Test with at least one desktop player and one browser video session, then check seek and repeat capability separately because support depends on the media source.

### WSL source and Windows build workflow

If Claude is editing the repository in WSL, keep the canonical source in the Linux filesystem (for example `/home/<user>/music-sw`) and build/run from a regular Windows drive path (for example `C:\Users\<user>\source\GameMusicFlyout-build`). Do not set a Windows command's working directory to `\\wsl.localhost\...`; some Windows tools reject UNC working directories. Microsoft also recommends keeping files on the filesystem used by the tools doing most of the work. For this Windows-only WPF app, compiling and running from NTFS avoids the WSL UNC boundary at build time.

A small sync-and-build script is reasonable. It should:

1. Copy source files from WSL to one dedicated Windows staging directory. Exclude `.git`, `bin`, and `obj`; do not copy build outputs back into the WSL source tree.
2. Remove stale files from that dedicated staging directory during sync, so deleted/renamed source files cannot remain and accidentally affect the build. Never use a mirror/delete operation against a directory containing user files.
3. Invoke `dotnet build` with the Windows staging directory as its working directory, using a Windows path. Keep the staging folder separate from the canonical repository so it is clear which copy is authoritative.
4. Report copy/build errors and the Windows path to the produced executable. Do not claim runtime validation unless the app was actually launched on Windows.

For a tiny project, a Windows-native canonical checkout at `C:\...` is also valid and is operationally simpler if Claude and all development tools run on Windows. Avoid having two editable “canonical” copies; pick one source of truth. If the source remains in WSL, the Windows folder is only a disposable build mirror.

Useful Microsoft references:

- [GlobalSystemMediaTransportControlsSession](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession)
- [Playback controls and supported commands](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionplaybackcontrols)
- [Change playback position](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.trychangeplaybackpositionasync)
- [Windows App SDK overview](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/)

## Prompt to continue with Claude

Read this document as the product and architecture specification. Implement the MVP in this repository using the recommended stack. First inspect the repository and the installed .NET/Windows build environment, then create the solution and implement it in small coherent steps. Do not replace GSMTC with browser scraping, audio capture, or simulated clicks. Treat unsupported source capabilities as normal and disable the relevant controls. Build and run the app on Windows where possible; if this environment is not Windows, clearly list what could not be verified and provide exact Windows build/run instructions. Keep the UI focused on title/artist or source, play/pause, previous, next, repeat, and seek.

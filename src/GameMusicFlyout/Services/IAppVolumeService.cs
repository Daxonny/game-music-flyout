using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>
/// Controls the Windows per-app volume (as in the Volume Mixer) of the media session's app.
/// Volume and mute only; no audio is captured. Must be used on the UI thread.
/// </summary>
public interface IAppVolumeService : IDisposable
{
    AppVolumeState State { get; }

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler<AppVolumeState>? StateChanged;

    /// <summary>Targets the app behind a media session (its SourceAppUserModelId), or nothing.</summary>
    void SetTargetApp(string? sourceAppId);

    /// <summary>
    /// Looks for the target's audio sessions again shortly (debounced). Call when the app may have
    /// started producing sound, e.g. playback started or the flyout was shown.
    /// </summary>
    void RequestRefresh();

    void SetVolume(float volume);

    void SetMute(bool isMuted);
}

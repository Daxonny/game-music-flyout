namespace GameMusicFlyout.Models;

public enum PlaybackState
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

public enum RepeatMode
{
    None,
    Track,
    List,
}

/// <summary>
/// Playback status, advertised controls, and timeline for the current media session.
/// </summary>
/// <param name="RepeatMode">Null when the source does not report a repeat mode.</param>
/// <param name="Position">Position relative to the timeline start, as of <paramref name="LastUpdated"/>.</param>
/// <param name="Duration">Timeline length; zero or negative when the source reports none (e.g. live streams).</param>
/// <param name="PlaybackRate">Null when the source does not report a rate.</param>
public sealed record PlaybackSnapshot(
    PlaybackState State,
    bool CanPlay,
    bool CanPause,
    bool CanTogglePlayPause,
    bool CanPrevious,
    bool CanNext,
    bool CanSeek,
    bool CanRepeat,
    RepeatMode? RepeatMode,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdated,
    double? PlaybackRate);

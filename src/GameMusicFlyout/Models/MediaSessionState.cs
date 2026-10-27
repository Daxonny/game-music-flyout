namespace GameMusicFlyout.Models;

/// <summary>Everything the UI needs about the current media session, published as one immutable snapshot.</summary>
/// <param name="IsAvailable">False when the Windows media session manager could not be obtained.</param>
/// <param name="Track">Null when there is no current session.</param>
/// <param name="Playback">Null when there is no current session or its playback info could not be read.</param>
public sealed record MediaSessionState(bool IsAvailable, TrackSnapshot? Track, PlaybackSnapshot? Playback)
{
    public static MediaSessionState NoSession { get; } = new(true, null, null);
    public static MediaSessionState Unavailable { get; } = new(false, null, null);

    public bool HasSession => Track is not null;
}

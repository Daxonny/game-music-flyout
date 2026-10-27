namespace GameMusicFlyout.Models;

/// <summary>
/// Normalized metadata for the current media session. Text fields are null when the source
/// does not publish them (never empty/whitespace).
/// </summary>
/// <param name="SourceAppId">The session's SourceAppUserModelId, e.g. "Spotify.exe" or "Chrome".</param>
public sealed record TrackSnapshot(
    string? Title,
    string? Artist,
    string? AlbumTitle,
    string SourceAppId);

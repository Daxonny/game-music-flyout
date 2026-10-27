using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>
/// Tracks the current Windows media session. Single-session for the MVP; the snapshot model
/// leaves room for a future source picker.
/// </summary>
public interface IMediaSessionService : IDisposable
{
    MediaSessionState CurrentState { get; }

    /// <summary>Raised on a background thread whenever <see cref="CurrentState"/> changes.</summary>
    event EventHandler<MediaSessionState>? StateChanged;

    /// <summary>Connects to Windows media controls. Never throws; failures publish an unavailable state.</summary>
    Task InitializeAsync();
}

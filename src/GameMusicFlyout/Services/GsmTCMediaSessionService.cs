using System.Diagnostics;
using GameMusicFlyout.Models;
using Windows.Media;
using Windows.Media.Control;

namespace GameMusicFlyout.Services;

/// <summary>
/// <see cref="IMediaSessionService"/> backed by GlobalSystemMediaTransportControlsSessionManager (GSMTC).
/// Event-driven: no metadata polling. All WinRT calls are guarded because sessions can vanish at any time.
/// </summary>
public sealed class GsmTCMediaSessionService : IMediaSessionService
{
    // Serializes session switches and refreshes so snapshots are published in order.
    // Not disposed on purpose: in-flight event handlers may still be waiting on it during shutdown.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private SessionSubscription? _subscription;
    private TrackSnapshot? _track;
    private PlaybackSnapshot? _playback;
    private volatile bool _disposed;

    public MediaSessionState CurrentState { get; private set; } = MediaSessionState.NoSession;

    public event EventHandler<MediaSessionState>? StateChanged;

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GSMTC unavailable: {ex}");
            Publish(MediaSessionState.Unavailable);
            return;
        }

        if (_disposed)
        {
            return;
        }

        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        // CurrentSessionChanged is not always raised when the current session closes; re-check on list changes too.
        _manager.SessionsChanged += OnSessionsChanged;

        await SwitchToCurrentSessionAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
        }

        _subscription?.Dispose();
        _subscription = null;
        StateChanged = null;
    }

    private async void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        await SwitchToCurrentSessionAsync();

    private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        await SwitchToCurrentSessionAsync();

    /// <summary>Never throws (called from async void event handlers).</summary>
    private async Task SwitchToCurrentSessionAsync()
    {
        try
        {
            await _lock.WaitAsync();
            try
            {
                if (_disposed || _manager is null)
                {
                    return;
                }

                GlobalSystemMediaTransportControlsSession? next;
                try
                {
                    next = _manager.GetCurrentSession();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GetCurrentSession failed: {ex}");
                    next = null;
                }

                if (next is not null && _subscription is not null && ReferenceEquals(next, _subscription.Session))
                {
                    return;
                }

                _subscription?.Dispose();
                _subscription = null;
                _track = null;
                _playback = null;

                if (next is not null)
                {
                    _subscription = new SessionSubscription(next, OnSessionEvent);
                    _track = await ReadTrackAsync(next);
                    _playback = ReadPlayback(next);
                }

                PublishCurrent();
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Session switch failed: {ex}");
        }
    }

    private async void OnSessionEvent(SessionSubscription source, SessionEventKind kind)
    {
        try
        {
            await _lock.WaitAsync();
            try
            {
                // Ignore late events from a session we already switched away from.
                if (_disposed || !ReferenceEquals(source, _subscription))
                {
                    return;
                }

                if (kind == SessionEventKind.MediaProperties)
                {
                    _track = await ReadTrackAsync(source.Session);
                }
                else
                {
                    _playback = ReadPlayback(source.Session);
                }

                if (ReferenceEquals(source, _subscription))
                {
                    PublishCurrent();
                }
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Session refresh failed: {ex}");
        }
    }

    private void PublishCurrent() =>
        Publish(_track is null ? MediaSessionState.NoSession : new MediaSessionState(true, _track, _playback));

    private void Publish(MediaSessionState state)
    {
        if (_disposed)
        {
            return;
        }

        CurrentState = state;
        StateChanged?.Invoke(this, state);
    }

    private static async Task<TrackSnapshot> ReadTrackAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var sourceAppId = string.Empty;
        try
        {
            sourceAppId = session.SourceAppUserModelId ?? string.Empty;
            var properties = await session.TryGetMediaPropertiesAsync();
            return new TrackSnapshot(
                Normalize(properties?.Title),
                Normalize(properties?.Artist),
                Normalize(properties?.AlbumTitle),
                sourceAppId);
        }
        catch (Exception ex)
        {
            // Session closing or player not ready; keep the session with unknown metadata.
            Debug.WriteLine($"Reading media properties failed: {ex}");
            return new TrackSnapshot(null, null, null, sourceAppId);
        }
    }

    private static PlaybackSnapshot? ReadPlayback(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var info = session.GetPlaybackInfo();
            var controls = info.Controls;
            var timeline = session.GetTimelineProperties();

            var repeatMode = info.AutoRepeatMode switch
            {
                null => (RepeatMode?)null,
                MediaPlaybackAutoRepeatMode.Track => RepeatMode.Track,
                MediaPlaybackAutoRepeatMode.List => RepeatMode.List,
                _ => RepeatMode.None,
            };

            return new PlaybackSnapshot(
                MapState(info.PlaybackStatus),
                controls.IsPlayEnabled,
                controls.IsPauseEnabled,
                controls.IsPlayPauseToggleEnabled,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                controls.IsPlaybackPositionEnabled,
                controls.IsRepeatEnabled,
                repeatMode,
                timeline.Position - timeline.StartTime,
                timeline.EndTime - timeline.StartTime,
                timeline.LastUpdatedTime,
                info.PlaybackRate);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Reading playback info failed: {ex}");
            return null;
        }
    }

    private static PlaybackState MapState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackState.Closed,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => PlaybackState.Opened,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => PlaybackState.Changing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        _ => PlaybackState.Unknown,
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private enum SessionEventKind
    {
        MediaProperties,
        PlaybackInfo,
        Timeline,
    }

    /// <summary>Owns the event handlers for one session so they can be detached together on switch/shutdown.</summary>
    private sealed class SessionSubscription : IDisposable
    {
        private readonly Action<SessionSubscription, SessionEventKind> _onEvent;

        public SessionSubscription(GlobalSystemMediaTransportControlsSession session, Action<SessionSubscription, SessionEventKind> onEvent)
        {
            Session = session;
            _onEvent = onEvent;
            Session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            Session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            Session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        public GlobalSystemMediaTransportControlsSession Session { get; }

        public void Dispose()
        {
            try
            {
                Session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                Session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                Session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            catch (Exception ex)
            {
                // The session's process may already be gone.
                Debug.WriteLine($"Unsubscribing session events failed: {ex}");
            }
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
            _onEvent(this, SessionEventKind.MediaProperties);

        private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
            _onEvent(this, SessionEventKind.PlaybackInfo);

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
            _onEvent(this, SessionEventKind.Timeline);
    }
}

using System.Diagnostics;
using System.Windows.Threading;
using GameMusicFlyout.Infrastructure;
using GameMusicFlyout.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace GameMusicFlyout.Services;

/// <summary>
/// <see cref="IAppVolumeService"/> on Core Audio (via NAudio.Wasapi). Finds the target app's audio sessions on
/// every active output device and sets their SimpleAudioVolume, which is what the Volume Mixer shows.
/// Sessions are looked up on demand (target change, <see cref="RequestRefresh"/>), not via session-created
/// notifications, which are unreliable when registered from the STA UI thread.
/// </summary>
public sealed class AppVolumeService : IAppVolumeService
{
    // Gives a just-started player time to open its audio session before we look for it.
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(400);

    private readonly Dispatcher _dispatcher;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly SessionEvents _sessionEvents;
    private readonly DispatcherTimer _refreshTimer;
    private readonly List<MMDevice> _devices = [];
    private readonly List<AudioSessionControl> _sessions = [];
    private AudioSessionControl? _eventSource;
    private string? _sourceAppId;
    private bool _disposed;

    public AppVolumeService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _enumerator = new MMDeviceEnumerator();
        _sessionEvents = new SessionEvents(this);
        _refreshTimer = new DispatcherTimer(RefreshDelay, DispatcherPriority.Background, (_, _) =>
        {
            _refreshTimer!.Stop();
            Resolve();
        }, dispatcher);
        _refreshTimer.Stop();
    }

    public AppVolumeState State { get; private set; } = AppVolumeState.Unavailable;

    public event EventHandler<AppVolumeState>? StateChanged;

    public void SetTargetApp(string? sourceAppId)
    {
        if (_disposed || string.Equals(sourceAppId, _sourceAppId, StringComparison.Ordinal))
        {
            return;
        }

        _sourceAppId = sourceAppId;
        Resolve();
    }

    public void RequestRefresh()
    {
        if (_disposed)
        {
            return;
        }

        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void SetVolume(float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        // Like YouTube: raising the volume unmutes.
        var unmute = State.IsMuted && volume > 0;
        if (ApplyToSessions(session =>
            {
                session.SimpleAudioVolume.Volume = volume;
                if (unmute)
                {
                    session.SimpleAudioVolume.Mute = false;
                }
            }))
        {
            Publish(new AppVolumeState(true, volume, State.IsMuted && !unmute));
        }
    }

    public void SetMute(bool isMuted)
    {
        if (ApplyToSessions(session => session.SimpleAudioVolume.Mute = isMuted))
        {
            Publish(State with { IsMuted = isMuted });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        ReleaseSessions();
        _enumerator.Dispose();
        StateChanged = null;
    }

    private void Resolve()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseSessions();

        if (_sourceAppId is not null)
        {
            var matcher = new ProcessMatcher(_sourceAppId);
            try
            {
                foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    _devices.Add(device);
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (!session.IsSystemSoundsSession
                            && session.State != AudioSessionState.AudioSessionStateExpired
                            && matcher.Matches(session.GetProcessID))
                        {
                            _sessions.Add(session);
                        }
                        else
                        {
                            session.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Enumerating audio sessions failed: {ex}");
            }
        }

        if (_sessions.Count == 0)
        {
            Publish(AppVolumeState.Unavailable);
            return;
        }

        try
        {
            // One session is enough to follow external changes (Volume Mixer, the app's own controls).
            _eventSource = _sessions[0];
            _eventSource.RegisterEventClient(_sessionEvents);
            var volume = _eventSource.SimpleAudioVolume;
            Publish(new AppVolumeState(true, volume.Volume, volume.Mute));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Reading session volume failed: {ex}");
            ReleaseSessions();
            Publish(AppVolumeState.Unavailable);
        }
    }

    /// <summary>Applies a change to every matched session; on failure (e.g. the app exited) looks them up again.</summary>
    private bool ApplyToSessions(Action<AudioSessionControl> change)
    {
        if (_disposed || _sessions.Count == 0)
        {
            return false;
        }

        try
        {
            foreach (var session in _sessions)
            {
                change(session);
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Changing session volume failed: {ex}");
            Resolve();
            return false;
        }
    }

    private void ReleaseSessions()
    {
        if (_eventSource is not null)
        {
            try
            {
                _eventSource.UnRegisterEventClient(_sessionEvents);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unregistering session events failed: {ex}");
            }

            _eventSource = null;
        }

        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        _sessions.Clear();

        foreach (var device in _devices)
        {
            device.Dispose();
        }

        _devices.Clear();
    }

    private void Publish(AppVolumeState state)
    {
        if (_disposed || state == State)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>Matches audio session processes to a media session's app ID.</summary>
    private sealed class ProcessMatcher(string sourceAppId)
    {
        private readonly IReadOnlySet<string> _processNames = SourceAppNames.GetProcessNames(sourceAppId);
        private readonly bool _isPackagedAppId = sourceAppId.Contains('!');
        private readonly Dictionary<uint, bool> _cache = [];

        public bool Matches(uint processId)
        {
            if (processId == 0)
            {
                return false;
            }

            if (!_cache.TryGetValue(processId, out var matches))
            {
                matches = (TryGetProcessName(processId) is { } name && _processNames.Contains(name))
                    || (_isPackagedAppId && string.Equals(TryGetPackagedAppId(processId), sourceAppId, StringComparison.OrdinalIgnoreCase));
                _cache[processId] = matches;
            }

            return matches;
        }

        private static string? TryGetProcessName(uint processId)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                return process.ProcessName;
            }
            catch (Exception)
            {
                // Exited, or not accessible.
                return null;
            }
        }

        private static unsafe string? TryGetPackagedAppId(uint processId)
        {
            var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                const int MaxLength = 512;
                var length = (uint)MaxLength;
                var buffer = stackalloc char[MaxLength];
                return NativeMethods.GetApplicationUserModelId(handle, ref length, buffer) == 0 && length > 0
                    ? new string(buffer, 0, (int)length - 1) // length includes the terminating null
                    : null;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
    }

    /// <summary>Core Audio session callbacks; they arrive on a background thread.</summary>
    private sealed class SessionEvents(AppVolumeService owner) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted) =>
            owner._dispatcher.BeginInvoke(() =>
            {
                // Ignore a late event from sessions that were released meanwhile.
                if (owner._eventSource is not null)
                {
                    owner.Publish(new AppVolumeState(true, volume, isMuted));
                }
            });

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
            owner._dispatcher.BeginInvoke(owner.RequestRefresh);

        public void OnStateChanged(AudioSessionState state)
        {
            if (state == AudioSessionState.AudioSessionStateExpired)
            {
                owner._dispatcher.BeginInvoke(owner.RequestRefresh);
            }
        }

        public void OnDisplayNameChanged(string displayName)
        {
        }

        public void OnIconPathChanged(string iconPath)
        {
        }

        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex)
        {
        }

        public void OnGroupingParamChanged(ref Guid groupingId)
        {
        }
    }
}

using System.Windows.Threading;
using GameMusicFlyout.Infrastructure;
using GameMusicFlyout.Models;
using GameMusicFlyout.Services;

namespace GameMusicFlyout.ViewModels;

/// <summary>
/// State and commands for the flyout card, projected from <see cref="IMediaSessionService"/> snapshots.
/// </summary>
public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    // Segoe MDL2 Assets glyphs (ships with Windows 10+).
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string RepeatAllGlyph = "\uE8EE";
    private const string RepeatOneGlyph = "\uE8ED";
    private const string MuteGlyph = "\uE74F";
    private const string VolumeLowGlyph = "\uE993";
    private const string VolumeMediumGlyph = "\uE994";
    private const string VolumeHighGlyph = "\uE995";

    private const double VolumeWheelStep = 5;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(4);

    // A press-and-release that moves less than this (e.g. a click on the thumb itself) doesn't seek.
    private const double MinSeekDeltaSeconds = 0.5;

    // Sources can report odd LastUpdatedTime values; outside this window the arrival time is used instead.
    private static readonly TimeSpan MaxTimestampAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaxTimestampSkew = TimeSpan.FromSeconds(2);

    private readonly IMediaSessionService _mediaService;
    private readonly IAppVolumeService _volumeService;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _statusTimer;

    private TrackSnapshot? _track;
    private PlaybackSnapshot? _playback;
    private TransportControls _failedControls;

    // Progress is interpolated locally from the last reported position instead of re-querying the OS.
    private double _anchorPositionSeconds;
    private DateTimeOffset _anchorTime;
    private bool _isUserSeeking;
    private double _seekStartSeconds;
    private bool _disposed;

    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string? _detailsToolTip;
    private string _statusText = string.Empty;
    private double _positionSeconds;
    private double _seekValue;
    private double _durationSeconds;
    private bool _canSeek;
    private bool _isPlaying;
    private bool _canPlayPause;
    private bool _canPrevious;
    private bool _canNext;
    private bool _canRepeat;
    private RepeatMode? _repeatMode;
    private AppVolumeState _volume = AppVolumeState.Unavailable;

    public FlyoutViewModel(IMediaSessionService mediaService, IAppVolumeService volumeService, Dispatcher dispatcher)
    {
        _mediaService = mediaService;
        _volumeService = volumeService;
        _dispatcher = dispatcher;

        _progressTimer = new DispatcherTimer(ProgressInterval, DispatcherPriority.Background, (_, _) => UpdatePosition(), dispatcher);
        _progressTimer.Stop();
        _statusTimer = new DispatcherTimer(StatusDuration, DispatcherPriority.Background, (_, _) => ClearStatus(), dispatcher);
        _statusTimer.Stop();

        PlayPauseCommand = new AsyncCommand(
            () => RunTransportAsync(TransportControls.PlayPause, "play/pause", _mediaService.TogglePlayPauseAsync),
            () => CanPlayPause,
            OnCommandError);
        PreviousCommand = new AsyncCommand(
            () => RunTransportAsync(TransportControls.Previous, "previous", _mediaService.PreviousAsync),
            () => CanPrevious,
            OnCommandError);
        NextCommand = new AsyncCommand(
            () => RunTransportAsync(TransportControls.Next, "next", _mediaService.NextAsync),
            () => CanNext,
            OnCommandError);
        CycleRepeatCommand = new AsyncCommand(
            () => RunTransportAsync(TransportControls.Repeat, "repeat", () => _mediaService.SetRepeatModeAsync(NextRepeatMode(RepeatMode))),
            () => CanRepeat,
            OnCommandError);

        ToggleMuteCommand = new AsyncCommand(
            () =>
            {
                ToggleMute();
                return Task.CompletedTask;
            },
            () => IsVolumeAvailable,
            OnCommandError);

        _volumeService.StateChanged += OnVolumeStateChanged;
        _mediaService.StateChanged += OnMediaStateChanged;
        Apply(_mediaService.CurrentState);
        ApplyVolume(_volumeService.State);
    }

    [Flags]
    private enum TransportControls
    {
        None = 0,
        PlayPause = 1,
        Previous = 2,
        Next = 4,
        Repeat = 8,
        Seek = 16,
    }

    public AsyncCommand PlayPauseCommand { get; }
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand CycleRepeatCommand { get; }
    public AsyncCommand ToggleMuteCommand { get; }

    public bool IsVolumeAvailable => _volume.IsAvailable;

    public bool IsMuted => _volume.IsMuted;

    /// <summary>0–100, two-way bound to the volume slider. Setting it changes the app's Windows volume.</summary>
    public double VolumePercent
    {
        get => Math.Round(_volume.Volume * 100);
        set
        {
            if (IsVolumeAvailable && Math.Abs(value - VolumePercent) >= 0.5)
            {
                _volumeService.SetVolume((float)(Math.Clamp(value, 0, 100) / 100));
            }
        }
    }

    public string VolumeGlyph => IsMuted || VolumePercent <= 0
        ? MuteGlyph
        : VolumePercent < 34 ? VolumeLowGlyph : VolumePercent < 67 ? VolumeMediumGlyph : VolumeHighGlyph;

    public string VolumeToolTip
    {
        get
        {
            var app = _track is null ? null : SourceAppNames.GetDisplayName(_track.SourceAppId);
            if (app is null)
            {
                return "Volume (nothing playing)";
            }

            if (!IsVolumeAvailable)
            {
                return $"No sound from {app} yet. Volume becomes available once it plays.";
            }

            return IsMuted
                ? $"{app} is muted (click to unmute)"
                : $"{app} volume: {VolumePercent:0}% (click to mute, scroll to adjust)";
        }
    }

    /// <summary>Called by the view for mouse-wheel steps over the volume control.</summary>
    public void ChangeVolumeBy(int wheelSteps)
    {
        if (IsVolumeAvailable && wheelSteps != 0)
        {
            VolumePercent = Math.Clamp(VolumePercent + (wheelSteps * VolumeWheelStep), 0, 100);
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>Artist, or the source app (e.g. "Chrome") when no artist is published.</summary>
    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    /// <summary>Album and source app, when known.</summary>
    public string? DetailsToolTip
    {
        get => _detailsToolTip;
        private set => SetProperty(ref _detailsToolTip, value);
    }

    /// <summary>Short, non-fatal error or info text; empty hides it.</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>Displayed position (drives the elapsed text). Follows the drag while the user seeks.</summary>
    public double PositionSeconds
    {
        get => _positionSeconds;
        private set
        {
            if (SetProperty(ref _positionSeconds, value))
            {
                OnPropertyChanged(nameof(ElapsedText));
            }
        }
    }

    /// <summary>
    /// Seek slider value (two-way bound so user input never detaches the binding).
    /// Separate from <see cref="PositionSeconds"/> because the slider clamps to its range.
    /// </summary>
    public double SeekValue
    {
        get => _seekValue;
        set
        {
            if (SetProperty(ref _seekValue, value) && _isUserSeeking)
            {
                PositionSeconds = value;
            }
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(TotalText));
                OnPropertyChanged(nameof(SeekMaximum));
            }
        }
    }

    /// <summary>Slider range; never 0, because a 0–0 slider renders as completely filled.</summary>
    public double SeekMaximum => DurationSeconds > 0 ? DurationSeconds : 1;

    public string ElapsedText => FormatTime(PositionSeconds);
    public string TotalText => DurationSeconds > 0 ? FormatTime(DurationSeconds) : "--:--";

    public bool CanSeek
    {
        get => _canSeek;
        private set => SetProperty(ref _canSeek, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => IsPlaying ? PauseGlyph : PlayGlyph;

    public bool CanPlayPause
    {
        get => _canPlayPause;
        private set => SetProperty(ref _canPlayPause, value);
    }

    public bool CanPrevious
    {
        get => _canPrevious;
        private set => SetProperty(ref _canPrevious, value);
    }

    public bool CanNext
    {
        get => _canNext;
        private set => SetProperty(ref _canNext, value);
    }

    public bool CanRepeat
    {
        get => _canRepeat;
        private set
        {
            if (SetProperty(ref _canRepeat, value))
            {
                OnPropertyChanged(nameof(RepeatToolTip));
            }
        }
    }

    public RepeatMode? RepeatMode
    {
        get => _repeatMode;
        private set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                OnPropertyChanged(nameof(IsRepeatActive));
                OnPropertyChanged(nameof(RepeatGlyph));
                OnPropertyChanged(nameof(RepeatToolTip));
            }
        }
    }

    public bool IsRepeatActive => RepeatMode is Models.RepeatMode.Track or Models.RepeatMode.List;

    public string RepeatGlyph => RepeatMode == Models.RepeatMode.Track ? RepeatOneGlyph : RepeatAllGlyph;

    public string RepeatToolTip => !CanRepeat
        ? IsFailed(TransportControls.Repeat) ? "Repeat failed for this player." : "Repeat isn't supported by this player."
        : RepeatMode switch
        {
            Models.RepeatMode.Track => "Repeat: track",
            Models.RepeatMode.List => "Repeat: list",
            _ => "Repeat: off",
        };

    /// <summary>Called by the view when the user presses on the seek bar.</summary>
    public void BeginSeek()
    {
        if (CanSeek)
        {
            _isUserSeeking = true;
            // PositionSeconds still holds the pre-press position: the slider moves its value before this runs.
            _seekStartSeconds = PositionSeconds;
        }
    }

    /// <summary>
    /// Called by the view when the user releases the seek bar. Issues a single seek for the final value;
    /// safe to call more than once per gesture.
    /// </summary>
    public async Task CommitSeekAsync()
    {
        if (!_isUserSeeking)
        {
            return;
        }

        _isUserSeeking = false;
        var target = Math.Clamp(SeekValue, 0, DurationSeconds);
        if (Math.Abs(target - _seekStartSeconds) < MinSeekDeltaSeconds)
        {
            UpdatePosition();
            return;
        }

        // Show the target immediately; the player's next timeline update confirms or corrects it.
        var previousAnchor = (_anchorPositionSeconds, _anchorTime);
        var optimisticTime = DateTimeOffset.Now;
        _anchorPositionSeconds = target;
        _anchorTime = optimisticTime;
        UpdatePosition();

        bool succeeded;
        try
        {
            succeeded = await _mediaService.SeekAsync(TimeSpan.FromSeconds(target));
        }
        catch (Exception ex)
        {
            OnCommandError(ex);
            succeeded = false;
        }

        if (!succeeded && !_disposed)
        {
            // Roll back only if no newer timeline update replaced the optimistic anchor meanwhile.
            if (_anchorPositionSeconds == target && _anchorTime == optimisticTime)
            {
                (_anchorPositionSeconds, _anchorTime) = previousAnchor;
            }

            ReportFailure(TransportControls.Seek, "seeking");
            UpdatePosition();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _mediaService.StateChanged -= OnMediaStateChanged;
        _volumeService.StateChanged -= OnVolumeStateChanged;
        _progressTimer.Stop();
        _statusTimer.Stop();
    }

    private void OnMediaStateChanged(object? sender, MediaSessionState state) =>
        // Raised on a background thread; never block it waiting for the UI.
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                Apply(state);
            }
        });

    private void Apply(MediaSessionState state)
    {
        if (!state.IsAvailable || state.Track is not { } track)
        {
            ResetSession();
            if (state.IsAvailable)
            {
                Title = "Nothing playing";
                Subtitle = "Start music or a video in any app.";
            }
            else
            {
                Title = "Media controls unavailable";
                Subtitle = "Windows didn't provide media session access.";
            }
        }
        else
        {
            var sameSession = _track is not null && _track.SourceAppId == track.SourceAppId;
            var sameItem = sameSession && _track!.Title == track.Title && _track.Artist == track.Artist;
            if (!sameItem)
            {
                // A failed control gets another chance with the next track or source.
                _failedControls = TransportControls.None;
                ClearStatus();
            }

            var startedPlaying = state.Playback?.State == PlaybackState.Playing && _playback?.State != PlaybackState.Playing;
            UpdateAnchor(state.Playback, sameSession);
            _track = track;
            _playback = state.Playback;
            ApplyTrackText(track);

            _volumeService.SetTargetApp(track.SourceAppId);
            if (startedPlaying && !IsVolumeAvailable)
            {
                // The app may only now be opening its audio session.
                _volumeService.RequestRefresh();
            }
        }

        UpdateCapabilities();
        UpdatePosition();
        UpdateProgressTimer();
        OnPropertyChanged(nameof(VolumeToolTip));
    }

    private void OnVolumeStateChanged(object? sender, AppVolumeState state)
    {
        if (!_disposed)
        {
            ApplyVolume(state);
        }
    }

    private void ApplyVolume(AppVolumeState state)
    {
        _volume = state;
        OnPropertyChanged(nameof(IsVolumeAvailable));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeGlyph));
        OnPropertyChanged(nameof(VolumeToolTip));
        ToggleMuteCommand.RaiseCanExecuteChanged();
    }

    private void ToggleMute()
    {
        if (!IsVolumeAvailable)
        {
            return;
        }

        if (IsMuted)
        {
            _volumeService.SetMute(false);
            if (VolumePercent <= 0)
            {
                // Unmuting at 0% would still be silent.
                _volumeService.SetVolume(0.5f);
            }
        }
        else
        {
            _volumeService.SetMute(true);
        }
    }

    private void ResetSession()
    {
        _volumeService.SetTargetApp(null);
        _track = null;
        _playback = null;
        _failedControls = TransportControls.None;
        _isUserSeeking = false;
        _anchorPositionSeconds = 0;
        _anchorTime = DateTimeOffset.Now;
        DetailsToolTip = null;
        ClearStatus();
    }

    private void ApplyTrackText(TrackSnapshot track)
    {
        var sourceName = SourceAppNames.GetDisplayName(track.SourceAppId);

        Title = track.Title ?? "Unknown title";
        // Show the artist when published; otherwise identify the source. Never derive an artist from the title.
        Subtitle = track.Artist ?? sourceName;

        var details = new List<string>();
        if (track.AlbumTitle is not null)
        {
            details.Add($"Album: {track.AlbumTitle}");
        }

        details.Add($"Source: {sourceName}");
        DetailsToolTip = string.Join(Environment.NewLine, details);
    }

    /// <summary>Must run before <see cref="_playback"/> is replaced: it may estimate from the old snapshot.</summary>
    private void UpdateAnchor(PlaybackSnapshot? next, bool sameSession)
    {
        var now = DateTimeOffset.Now;
        var previous = sameSession ? _playback : null;

        if (next is null)
        {
            _anchorPositionSeconds = 0;
            _anchorTime = now;
            return;
        }

        var timelineChanged = previous is null
            || previous.Position != next.Position
            || previous.LastUpdated != next.LastUpdated
            || previous.Duration != next.Duration;

        if (timelineChanged)
        {
            _anchorPositionSeconds = next.Position.TotalSeconds;
            _anchorTime = SanitizeTimestamp(next.LastUpdated, now);
        }
        else if ((previous!.State == PlaybackState.Playing) != (next.State == PlaybackState.Playing))
        {
            // Play/pause without a timeline update: continue from where the local estimate is now.
            _anchorPositionSeconds = EstimatePositionSeconds(now);
            _anchorTime = now;
        }
    }

    private double EstimatePositionSeconds(DateTimeOffset now)
    {
        if (_playback is null)
        {
            return 0;
        }

        var position = _anchorPositionSeconds;
        if (_playback.State == PlaybackState.Playing)
        {
            var rate = _playback.PlaybackRate is double r && r > 0 ? r : 1.0;
            position += Math.Max(0, (now - _anchorTime).TotalSeconds) * rate;
        }

        var duration = _playback.Duration.TotalSeconds;
        return duration > 0 ? Math.Clamp(position, 0, duration) : Math.Max(0, position);
    }

    private void UpdatePosition()
    {
        if (_isUserSeeking)
        {
            return;
        }

        var position = EstimatePositionSeconds(DateTimeOffset.Now);
        PositionSeconds = position;
        SeekValue = DurationSeconds > 0 ? position : 0;
    }

    private void UpdateCapabilities()
    {
        var playback = _playback;

        IsPlaying = playback?.State == PlaybackState.Playing;
        CanPlayPause = playback is not null
            && (playback.CanTogglePlayPause || (IsPlaying ? playback.CanPause : playback.CanPlay))
            && !IsFailed(TransportControls.PlayPause);
        CanPrevious = playback is { CanPrevious: true } && !IsFailed(TransportControls.Previous);
        CanNext = playback is { CanNext: true } && !IsFailed(TransportControls.Next);
        RepeatMode = playback?.RepeatMode;
        CanRepeat = playback is { CanRepeat: true, RepeatMode: not null } && !IsFailed(TransportControls.Repeat);
        OnPropertyChanged(nameof(RepeatToolTip));

        var duration = playback?.Duration.TotalSeconds ?? 0;
        DurationSeconds = duration > 0 ? duration : 0;
        CanSeek = duration > 0 && playback!.CanSeek && !IsFailed(TransportControls.Seek);
        if (!CanSeek)
        {
            _isUserSeeking = false;
        }

        PlayPauseCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        CycleRepeatCommand.RaiseCanExecuteChanged();
    }

    private void UpdateProgressTimer()
    {
        if (!_disposed && _playback?.State == PlaybackState.Playing)
        {
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    private async Task RunTransportAsync(TransportControls control, string actionName, Func<Task<bool>> operation)
    {
        if (!await operation() && !_disposed)
        {
            ReportFailure(control, actionName);
        }
    }

    private void ReportFailure(TransportControls control, string actionName)
    {
        _failedControls |= control;
        var source = _track is null ? "The player" : SourceAppNames.GetDisplayName(_track.SourceAppId);
        ShowStatus($"{source} didn't accept {actionName}.");
        UpdateCapabilities();
    }

    private bool IsFailed(TransportControls control) => (_failedControls & control) != 0;

    private void OnCommandError(Exception ex) => ShowStatus($"Something went wrong: {ex.Message}");

    private void ShowStatus(string message)
    {
        StatusText = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void ClearStatus()
    {
        _statusTimer.Stop();
        StatusText = string.Empty;
    }

    private static RepeatMode NextRepeatMode(RepeatMode? current) => current switch
    {
        Models.RepeatMode.Track => Models.RepeatMode.List,
        Models.RepeatMode.List => Models.RepeatMode.None,
        _ => Models.RepeatMode.Track,
    };

    private static DateTimeOffset SanitizeTimestamp(DateTimeOffset reported, DateTimeOffset now) =>
        reported > now + MaxTimestampSkew || reported < now - MaxTimestampAge ? now : reported;

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}

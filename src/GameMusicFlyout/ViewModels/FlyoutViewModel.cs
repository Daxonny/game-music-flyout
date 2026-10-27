using System.Windows.Threading;
using GameMusicFlyout.Infrastructure;
using GameMusicFlyout.Models;
using GameMusicFlyout.Services;

namespace GameMusicFlyout.ViewModels;

/// <summary>
/// State and commands for the flyout card, projected from <see cref="IMediaSessionService"/> snapshots.
/// Step 2: display only; the transport commands are wired in step 3.
/// </summary>
public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    // Segoe MDL2 Assets glyphs (ships with Windows 10+).
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string RepeatAllGlyph = "";
    private const string RepeatOneGlyph = "";

    private readonly IMediaSessionService _mediaService;
    private readonly Dispatcher _dispatcher;

    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string? _detailsToolTip;
    private string _statusText = string.Empty;
    private double _positionSeconds;
    private double _durationSeconds;
    private bool _canSeek;
    private bool _isPlaying;
    private bool _canPlayPause;
    private bool _canPrevious;
    private bool _canNext;
    private bool _canRepeat;
    private RepeatMode? _repeatMode;

    public FlyoutViewModel(IMediaSessionService mediaService, Dispatcher dispatcher)
    {
        _mediaService = mediaService;
        _dispatcher = dispatcher;

        PlayPauseCommand = new AsyncCommand(() => Task.CompletedTask, () => CanPlayPause);
        PreviousCommand = new AsyncCommand(() => Task.CompletedTask, () => CanPrevious);
        NextCommand = new AsyncCommand(() => Task.CompletedTask, () => CanNext);
        CycleRepeatCommand = new AsyncCommand(() => Task.CompletedTask, () => CanRepeat);

        _mediaService.StateChanged += OnMediaStateChanged;
        Apply(_mediaService.CurrentState);
    }

    public AsyncCommand PlayPauseCommand { get; }
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand CycleRepeatCommand { get; }

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

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetProperty(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(TotalText));
            }
        }
    }

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
        ? "Repeat isn't supported by this player."
        : RepeatMode switch
        {
            Models.RepeatMode.Track => "Repeat: track",
            Models.RepeatMode.List => "Repeat: list",
            _ => "Repeat: off",
        };

    public void Dispose() => _mediaService.StateChanged -= OnMediaStateChanged;

    private void OnMediaStateChanged(object? sender, MediaSessionState state) =>
        // Raised on a background thread; never block it waiting for the UI.
        _dispatcher.InvokeAsync(() => Apply(state));

    private void Apply(MediaSessionState state)
    {
        if (!state.IsAvailable)
        {
            ApplyIdle("Media controls unavailable", "Windows didn't provide media session access.");
        }
        else if (state.Track is not { } track)
        {
            ApplyIdle("Nothing playing", "Start music or a video in any app.");
        }
        else
        {
            ApplyTrack(track);
            ApplyPlayback(state.Playback);
        }

        PlayPauseCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        CycleRepeatCommand.RaiseCanExecuteChanged();
    }

    private void ApplyIdle(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
        DetailsToolTip = null;
        ApplyPlayback(null);
    }

    private void ApplyTrack(TrackSnapshot track)
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

    private void ApplyPlayback(PlaybackSnapshot? playback)
    {
        if (playback is null)
        {
            IsPlaying = false;
            CanPlayPause = false;
            CanPrevious = false;
            CanNext = false;
            CanRepeat = false;
            RepeatMode = null;
            CanSeek = false;
            DurationSeconds = 0;
            PositionSeconds = 0;
            return;
        }

        IsPlaying = playback.State == PlaybackState.Playing;
        CanPlayPause = playback.CanTogglePlayPause || (IsPlaying ? playback.CanPause : playback.CanPlay);
        CanPrevious = playback.CanPrevious;
        CanNext = playback.CanNext;
        CanRepeat = playback.CanRepeat && playback.RepeatMode is not null;
        RepeatMode = playback.RepeatMode;

        var duration = playback.Duration.TotalSeconds;
        var hasDuration = duration > 0;
        DurationSeconds = hasDuration ? duration : 0;
        PositionSeconds = hasDuration
            ? Math.Clamp(playback.Position.TotalSeconds, 0, duration)
            : Math.Max(0, playback.Position.TotalSeconds);
        CanSeek = hasDuration && playback.CanSeek;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}

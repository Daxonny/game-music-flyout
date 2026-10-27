using GameMusicFlyout.Infrastructure;

namespace GameMusicFlyout.ViewModels;

/// <summary>
/// State and commands for the flyout card.
/// Step 1: static sample data only; step 2 wires it to the media session service.
/// </summary>
public sealed class FlyoutViewModel : ObservableObject
{
    // Segoe MDL2 Assets glyphs (ships with Windows 10+).
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string RepeatAllGlyph = "";
    private const string RepeatOneGlyph = "";

    private string _title = "Manifest";
    private string _subtitle = "Sample Artist";
    private string _statusText = string.Empty;
    private double _positionSeconds = 83;
    private double _durationSeconds = 214;
    private bool _canSeek = true;
    private bool _isPlaying = true;
    private bool _isRepeatActive;
    private bool _isRepeatOne;

    public FlyoutViewModel()
    {
        PlayPauseCommand = new AsyncCommand(() => Task.CompletedTask);
        PreviousCommand = new AsyncCommand(() => Task.CompletedTask);
        NextCommand = new AsyncCommand(() => Task.CompletedTask);
        CycleRepeatCommand = new AsyncCommand(() => Task.CompletedTask);
    }

    public AsyncCommand PlayPauseCommand { get; }
    public AsyncCommand PreviousCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand CycleRepeatCommand { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    /// <summary>Artist, or the source app (e.g. "YouTube · Chrome") when no artist is published.</summary>
    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    /// <summary>Short, non-fatal error or info text; empty hides it.</summary>
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
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
        set
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
        set => SetProperty(ref _canSeek, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => IsPlaying ? PauseGlyph : PlayGlyph;

    public bool IsRepeatActive
    {
        get => _isRepeatActive;
        set => SetProperty(ref _isRepeatActive, value);
    }

    public bool IsRepeatOne
    {
        get => _isRepeatOne;
        set
        {
            if (SetProperty(ref _isRepeatOne, value))
            {
                OnPropertyChanged(nameof(RepeatGlyph));
            }
        }
    }

    public string RepeatGlyph => IsRepeatOne ? RepeatOneGlyph : RepeatAllGlyph;

    public string RepeatToolTip => "Repeat";

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }
}

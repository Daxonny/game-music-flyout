namespace GameMusicFlyout.Models;

/// <summary>User settings persisted to %APPDATA%\GameMusicFlyout\settings.json.</summary>
public sealed class AppSettings
{
    public const double MinScale = 0.75;
    public const double MaxScale = 1.5;
    public const double ScaleStep = 0.05;

    /// <summary>Window position in WPF units; null until the user has moved or shown the flyout once.</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Scale { get; set; } = 1.0;

    public string Hotkey { get; set; } = Models.Hotkey.Default.ToString();

    public bool ShowOnStartup { get; set; } = true;

    public static double ClampScale(double scale) =>
        double.IsFinite(scale)
            ? Math.Round(Math.Clamp(scale, MinScale, MaxScale) / ScaleStep) * ScaleStep
            : 1.0;
}

namespace GameMusicFlyout.Services;

/// <summary>Shows, hides, places, and scales the flyout window.</summary>
public interface IOverlayWindowService
{
    bool IsShown { get; }

    /// <summary>Proportional card scale, clamped to the range in <see cref="Models.AppSettings"/>.</summary>
    double Scale { get; set; }

    /// <summary>Raised when <see cref="IsShown"/> or <see cref="Scale"/> changes.</summary>
    event EventHandler? StateChanged;

    void Show();

    void Hide();

    void Toggle();

    /// <summary>Writes the current position and scale to the settings file.</summary>
    void SaveState();
}

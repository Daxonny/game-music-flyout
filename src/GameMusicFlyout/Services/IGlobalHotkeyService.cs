using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>One system-wide shortcut that works while another app (e.g. a game) has focus.</summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>The registered shortcut, or null when none is registered.</summary>
    Hotkey? Current { get; }

    /// <summary>Raised on the UI thread when the shortcut is pressed.</summary>
    event EventHandler? Pressed;

    /// <summary>
    /// Replaces the current shortcut. On failure the previous shortcut stays registered (if it was)
    /// and <paramref name="error"/> explains why, e.g. another app already uses the combination.
    /// </summary>
    bool TryRegister(Hotkey hotkey, out string? error);
}

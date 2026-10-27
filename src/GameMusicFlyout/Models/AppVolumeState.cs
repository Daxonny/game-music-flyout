namespace GameMusicFlyout.Models;

/// <summary>Windows per-app (Volume Mixer) volume of the app that owns the current media session.</summary>
/// <param name="IsAvailable">False when there is no target app or it has no audio session yet.</param>
/// <param name="Volume">0.0–1.0.</param>
public sealed record AppVolumeState(bool IsAvailable, float Volume, bool IsMuted)
{
    public static AppVolumeState Unavailable { get; } = new(false, 0, false);
}

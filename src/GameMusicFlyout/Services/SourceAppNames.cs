using System.IO;

namespace GameMusicFlyout.Services;

/// <summary>
/// Turns a media session's SourceAppUserModelId into a readable app name.
/// GSMTC does not expose which website is playing, so browsers resolve to the browser name only.
/// </summary>
public static class SourceAppNames
{
    // Matched case-insensitively as substrings of the app ID, in order (first match wins).
    private static readonly (string Fragment, string Name)[] KnownApps =
    [
        ("spotify", "Spotify"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("308046B0AF4A39CB", "Firefox"), // Firefox registers a hashed AUMID for its default install.
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
        ("vlc", "VLC"),
        ("ZuneMusic", "Media Player"),
        ("ZuneVideo", "Movies & TV"),
        ("AppleMusic", "Apple Music"),
        ("iTunes", "iTunes"),
        ("TIDAL", "TIDAL"),
        ("Amazon Music", "Amazon Music"),
        ("AmazonMobileLLC.AmazonMusic", "Amazon Music"),
        ("Deezer", "Deezer"),
        ("foobar2000", "foobar2000"),
        ("MusicBee", "MusicBee"),
        ("AIMP", "AIMP"),
        ("SoundCloud", "SoundCloud"),
        ("YouTubeMusic", "YouTube Music"),
    ];

    // Process names (without .exe) that play audio for an app ID, matched like KnownApps. All matches apply.
    private static readonly (string Fragment, string ProcessName)[] KnownProcesses =
    [
        ("spotify", "Spotify"),
        ("msedge", "msedge"),
        ("chrome", "chrome"),
        ("firefox", "firefox"),
        ("308046B0AF4A39CB", "firefox"),
        ("brave", "brave"),
        ("opera", "opera"),
        ("vivaldi", "vivaldi"),
        ("vlc", "vlc"),
        ("ZuneMusic", "Microsoft.Media.Player"),
        ("ZuneVideo", "Video.UI"),
        ("AppleMusic", "AppleMusic"),
        ("iTunes", "iTunes"),
        ("TIDAL", "TIDAL"),
        ("Amazon Music", "Amazon Music"),
        ("Deezer", "Deezer"),
        ("foobar2000", "foobar2000"),
        ("MusicBee", "MusicBee"),
        ("AIMP", "AIMP"),
    ];

    /// <summary>
    /// Likely process names for an app ID, e.g. "chrome.exe" → chrome, "SpotifyAB...!Spotify" → Spotify.
    /// Packaged apps are additionally matched by their process AUMID (see AppVolumeService).
    /// </summary>
    public static IReadOnlySet<string> GetProcessNames(string sourceAppId)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var fileName = Path.GetFileName(sourceAppId);
        if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(fileName[..^4]);
        }

        foreach (var (fragment, processName) in KnownProcesses)
        {
            if (sourceAppId.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(processName);
            }
        }

        return names;
    }

    public static string GetDisplayName(string? sourceAppId)
    {
        if (string.IsNullOrWhiteSpace(sourceAppId))
        {
            return "Unknown app";
        }

        foreach (var (fragment, name) in KnownApps)
        {
            if (sourceAppId.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return MakeReadable(sourceAppId);
    }

    /// <summary>
    /// Best-effort fallback: "Vendor.App_hash!AppId" → "App", "C:\...\player.exe" → "player".
    /// </summary>
    private static string MakeReadable(string sourceAppId)
    {
        var name = sourceAppId;

        var bang = name.IndexOf('!');
        if (bang >= 0)
        {
            // Packaged app: take the package name part ("Vendor.App_hash") rather than the entry point.
            name = name[..bang];
            var underscore = name.IndexOf('_');
            if (underscore > 0)
            {
                name = name[..underscore];
            }

            var dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1)
            {
                name = name[(dot + 1)..];
            }
        }
        else
        {
            name = Path.GetFileName(name);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }
        }

        return string.IsNullOrWhiteSpace(name) ? sourceAppId : name;
    }
}

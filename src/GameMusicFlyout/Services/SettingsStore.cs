using System.Diagnostics;
using System.IO;
using System.Text.Json;
using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON. Never throws; a missing or corrupt file means defaults.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameMusicFlyout", "settings.json"))
    {
    }

    public SettingsStore(string path) => _path = path;

    public string FilePath => _path;

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Loading settings failed, using defaults: {ex}");
            settings = new AppSettings();
        }

        settings.Scale = AppSettings.ClampScale(settings.Scale);
        if (settings.Left is { } left && !double.IsFinite(left) || settings.Top is { } top && !double.IsFinite(top))
        {
            settings.Left = null;
            settings.Top = null;
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write then rename so a crash mid-write can't leave a truncated settings file.
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Saving settings failed: {ex}");
        }
    }
}

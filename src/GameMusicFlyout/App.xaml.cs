using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using GameMusicFlyout.Models;
using GameMusicFlyout.Services;
using GameMusicFlyout.ViewModels;

namespace GameMusicFlyout;

public partial class App : Application
{
    // Per-user, so another Windows account can run its own copy.
    private const string SingleInstanceMutexName = @"Local\GameMusicFlyout.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;
    private GsmTCMediaSessionService? _mediaService;
    private FlyoutViewModel? _viewModel;
    private OverlayWindowService? _overlay;
    private GlobalHotkeyService? _hotkeys;
    private TrayIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Game Music Flyout is already running. Look for its icon in the notification area (system tray).",
                "Game Music Flyout",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();

        _mediaService = new GsmTCMediaSessionService();
        _viewModel = new FlyoutViewModel(_mediaService, Dispatcher);
        var window = new MainWindow { DataContext = _viewModel };
        _overlay = new OverlayWindowService(window, _settings, _settingsStore);

        _tray = new TrayIcon();
        _tray.ToggleRequested += (_, _) => _overlay.Toggle();
        _tray.ScaleRequested += (_, scale) => _overlay.Scale = scale;
        _tray.ChangeHotkeyRequested += (_, _) => ShowHotkeyDialog();
        _tray.ShowOnStartupToggled += (_, value) =>
        {
            _settings.ShowOnStartup = value;
            _settingsStore.Save(_settings);
            UpdateTray();
        };
        _tray.ExitRequested += (_, _) => Shutdown();
        _overlay.StateChanged += (_, _) => UpdateTray();

        _hotkeys = new GlobalHotkeyService();
        _hotkeys.Pressed += (_, _) => _overlay.Toggle();
        var hotkeyRegistered = RegisterSavedHotkey();

        // Without a working shortcut the user needs to see the flyout (and the tray message) to fix it.
        if (_settings.ShowOnStartup || !hotkeyRegistered)
        {
            _overlay.Show();
        }

        UpdateTray();

        // Connecting to media sessions is async and never throws.
        await _mediaService.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlay?.SaveState();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _viewModel?.Dispose();
        _mediaService?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private bool RegisterSavedHotkey()
    {
        if (!Hotkey.TryParse(_settings!.Hotkey, out var hotkey))
        {
            hotkey = Hotkey.Default;
        }

        if (_hotkeys!.TryRegister(hotkey, out var error))
        {
            return true;
        }

        _tray!.ShowNotification(
            "Shortcut not available",
            $"{error} Right-click this icon and choose Shortcut to pick another.",
            System.Windows.Forms.ToolTipIcon.Warning);
        return false;
    }

    private void ShowHotkeyDialog()
    {
        var dialog = new HotkeyDialog(_hotkeys!.Current, hotkey =>
        {
            if (!_hotkeys.TryRegister(hotkey, out var error))
            {
                return error;
            }

            _settings!.Hotkey = hotkey.ToString();
            _settingsStore!.Save(_settings);
            return null;
        });
        dialog.ShowDialog();
        UpdateTray();
    }

    private void UpdateTray() =>
        _tray?.Update(_overlay!.IsShown, _overlay.Scale, _hotkeys?.Current, _settings!.ShowOnStartup);

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A small utility should keep running rather than vanish mid-game; record the error locally.
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameMusicFlyout", "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}] {e.Exception}{Environment.NewLine}");
        }
        catch (Exception logError)
        {
            Debug.WriteLine($"Writing error log failed: {logError}");
        }

        e.Handled = true;
    }
}

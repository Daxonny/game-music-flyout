using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GameMusicFlyout.Infrastructure;
using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>
/// Owns the flyout window's desktop behavior: never takes focus from the game (WS_EX_NOACTIVATE),
/// stays topmost, remembers its position, stays on a visible screen, and scales with Ctrl+mouse wheel.
/// Uses ordinary window layering only; no injection into games.
/// </summary>
public sealed class OverlayWindowService : IOverlayWindowService
{
    private const double ScreenMargin = 16;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly MainWindow _window;
    private readonly AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _saveTimer;
    private bool _hasBeenPlaced;

    public OverlayWindowService(MainWindow window, AppSettings settings, SettingsStore store)
    {
        _window = window;
        _settings = settings;
        _store = store;
        _saveTimer = new DispatcherTimer(SaveDelay, DispatcherPriority.Background, (_, _) => SaveState(), window.Dispatcher);
        _saveTimer.Stop();

        _window.ShowActivated = false;
        _window.ApplyScale(_settings.Scale);
        if (_settings.Left is { } left && _settings.Top is { } top)
        {
            _window.Left = left;
            _window.Top = top;
        }

        _window.SourceInitialized += OnSourceInitialized;
        _window.Loaded += OnLoaded;
        _window.SizeChanged += (_, _) => EnsureOnScreen();
        _window.LocationChanged += OnLocationChanged;
        _window.IsVisibleChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _window.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    public event EventHandler? StateChanged;

    public bool IsShown => _window.IsVisible;

    public double Scale
    {
        get => _settings.Scale;
        set
        {
            var scale = AppSettings.ClampScale(value);
            if (Math.Abs(scale - _settings.Scale) < 0.001)
            {
                return;
            }

            _settings.Scale = scale;
            _window.ApplyScale(scale);
            ScheduleSave();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Show()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        // Re-assert topmost without activating, so it lands above other topmost windows (e.g. some games).
        var handle = new WindowInteropHelper(_window).Handle;
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        EnsureOnScreen();
    }

    public void Hide()
    {
        if (_window.IsVisible)
        {
            _window.Hide();
            SaveState();
        }
    }

    public void Toggle()
    {
        if (IsShown)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    public void SaveState()
    {
        _saveTimer.Stop();
        if (_hasBeenPlaced && double.IsFinite(_window.Left) && double.IsFinite(_window.Top))
        {
            _settings.Left = _window.Left;
            _settings.Top = _window.Top;
        }

        _store.Save(_settings);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // No activation: clicks work, but keyboard focus stays with the game. Tool window: no taskbar/Alt+Tab entry.
        var handle = new WindowInteropHelper(_window).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.Left is null || _settings.Top is null)
        {
            // First run: bottom-right of the primary work area.
            var workArea = SystemParameters.WorkArea;
            _window.Left = workArea.Right - _window.ActualWidth - ScreenMargin;
            _window.Top = workArea.Bottom - _window.ActualHeight - ScreenMargin;
        }

        _hasBeenPlaced = true;
        EnsureOnScreen();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_hasBeenPlaced)
        {
            ScheduleSave();
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The flyout never has keyboard focus, so read Ctrl from the global key state.
        if ((NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) == 0)
        {
            return;
        }

        Scale += Math.Sign(e.Delta) * AppSettings.ScaleStep;
        e.Handled = true;
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Moves the window fully inside the work area of the nearest monitor (e.g. after a monitor was
    /// unplugged, the scale grew, or the status line made the card taller). Works in device pixels.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (!_window.IsVisible || !_hasBeenPlaced)
        {
            return;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromRect(rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var work = info.rcWork;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = Math.Max(work.Left, Math.Min(rect.Left, work.Right - width));
        var y = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - height));

        if (x != rect.Left || y != rect.Top)
        {
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }
}

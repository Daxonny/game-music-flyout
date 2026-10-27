using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using GameMusicFlyout.Infrastructure;
using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>
/// RegisterHotKey on a hidden message-only window, so the shortcut keeps working while the flyout is hidden.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int HotkeyId = 0x4D46; // Arbitrary, unique within this window.
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _source = new HwndSource(new HwndSourceParameters("GameMusicFlyoutHotkeys")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    public Hotkey? Current { get; private set; }

    public event EventHandler? Pressed;

    public bool TryRegister(Hotkey hotkey, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!hotkey.IsValid)
        {
            error = "Use Ctrl, Alt, or Win together with another key.";
            return false;
        }

        if (hotkey == Current)
        {
            error = null;
            return true;
        }

        var previous = Current;
        UnregisterCurrent();

        if (Register(hotkey, out error))
        {
            Current = hotkey;
            return true;
        }

        // Put the old shortcut back so a failed change doesn't leave the user without one.
        if (previous is not null && Register(previous, out _))
        {
            Current = previous;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterCurrent();
        _source.RemoveHook(WndProc);
        _source.Dispose();
        Pressed = null;
    }

    private bool Register(Hotkey hotkey, out string? error)
    {
        var modifiers = NativeMethods.MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= NativeMethods.MOD_CONTROL;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= NativeMethods.MOD_ALT;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= NativeMethods.MOD_SHIFT;
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= NativeMethods.MOD_WIN;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, virtualKey))
        {
            error = null;
            return true;
        }

        var code = Marshal.GetLastPInvokeError();
        error = code == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"{hotkey} is already used by another app."
            : $"Couldn't register {hotkey} (error {code}).";
        return false;
    }

    private void UnregisterCurrent()
    {
        if (Current is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            Current = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }
}

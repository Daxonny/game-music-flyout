using System.Windows;
using System.Windows.Input;
using GameMusicFlyout.Models;

namespace GameMusicFlyout;

/// <summary>
/// Captures a new global shortcut. <c>apply</c> tries to register it and returns an error message,
/// or null on success; on error the dialog stays open and the previous shortcut remains active.
/// </summary>
public partial class HotkeyDialog : Window
{
    private readonly Func<Hotkey, string?> _apply;
    private Hotkey? _candidate;

    public HotkeyDialog(Hotkey? current, Func<Hotkey, string?> apply)
    {
        InitializeComponent();
        _apply = apply;
        _candidate = current;
        ShortcutText.Text = current?.ToString() ?? "Press keys…";
        SaveButton.IsEnabled = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Every key goes to the capture, including Esc, Enter, and Alt combinations, so they can't trigger buttons.
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var candidate = new Hotkey(modifiers, key);

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Only modifiers so far: show progress, wait for the main key.
            ShortcutText.Text = modifiers == ModifierKeys.None ? "Press keys…" : $"{FormatModifiers(modifiers)}+…";
            return;
        }

        ShortcutText.Text = candidate.ToString();
        if (candidate.IsValid)
        {
            _candidate = candidate;
            SaveButton.IsEnabled = true;
            HideError();
        }
        else
        {
            _candidate = null;
            SaveButton.IsEnabled = false;
            ShowError("Include Ctrl, Alt, or Win, so the key still works normally in games and chat.");
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _candidate = Hotkey.Default;
        ShortcutText.Text = Hotkey.Default.ToString();
        SaveButton.IsEnabled = true;
        HideError();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_candidate is null)
        {
            return;
        }

        var error = _apply(_candidate);
        if (error is null)
        {
            DialogResult = true;
        }
        else
        {
            ShowError(error + " Try another combination.");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private static string FormatModifiers(ModifierKeys modifiers) =>
        new Hotkey(modifiers, Key.A).ToString()[..^2];
}

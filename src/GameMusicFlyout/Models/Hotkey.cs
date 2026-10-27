using System.Windows.Input;

namespace GameMusicFlyout.Models;

/// <summary>A global shortcut such as Ctrl+Alt+M. Stored in settings as its display string.</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static Hotkey Default { get; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.M);

    /// <summary>
    /// Shortcuts need Ctrl, Alt, or Win so a plain key (or Shift+key) never gets taken away from the game or chat.
    /// </summary>
    public bool IsValid =>
        (Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0
        && Key is not (Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyToString(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "alt":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "shift":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "win" or "windows":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (key is not null || !TryParseKey(raw, out var parsed))
                    {
                        return false;
                    }

                    key = parsed;
                    break;
            }
        }

        if (key is null)
        {
            return false;
        }

        var candidate = new Hotkey(modifiers, key.Value);
        if (!candidate.IsValid)
        {
            return false;
        }

        hotkey = candidate;
        return true;
    }

    private static string KeyToString(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => key.ToString(),
    };

    private static bool TryParseKey(string text, out Key key)
    {
        if (text.Length == 1 && char.IsAsciiDigit(text[0]))
        {
            key = Key.D0 + (text[0] - '0');
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}

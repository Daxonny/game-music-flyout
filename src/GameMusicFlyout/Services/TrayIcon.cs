using System.Drawing;
using System.Windows.Forms;
using GameMusicFlyout.Models;

namespace GameMusicFlyout.Services;

/// <summary>
/// Notification-area icon and menu. Raises intent events only; <see cref="App"/> decides what they do.
/// Left-click toggles the flyout.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private static readonly double[] ScalePresets = [0.75, 1.0, 1.25, 1.5];

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _sizeItem;
    private readonly ToolStripMenuItem _hotkeyItem;
    private readonly ToolStripMenuItem _showOnStartupItem;

    public TrayIcon()
    {
        var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        using (iconStream)
        {
            _icon = new Icon(iconStream, SystemInformation.SmallIconSize);
        }

        _toggleItem = new ToolStripMenuItem("Show flyout", null, (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty));
        _toggleItem.Font = new Font(_toggleItem.Font, System.Drawing.FontStyle.Bold);

        _sizeItem = new ToolStripMenuItem("Size");
        foreach (var preset in ScalePresets)
        {
            var item = new ToolStripMenuItem($"{preset:P0}") { Tag = preset };
            item.Click += (_, _) => ScaleRequested?.Invoke(this, preset);
            _sizeItem.DropDownItems.Add(item);
        }

        _sizeItem.DropDownItems.Add(new ToolStripSeparator());
        _sizeItem.DropDownItems.Add(new ToolStripMenuItem("Tip: Ctrl + mouse wheel over the flyout") { Enabled = false });

        _hotkeyItem = new ToolStripMenuItem("Shortcut…", null, (_, _) => ChangeHotkeyRequested?.Invoke(this, EventArgs.Empty));
        _showOnStartupItem = new ToolStripMenuItem("Show flyout when the app starts", null,
            (_, _) => ShowOnStartupToggled?.Invoke(this, !_showOnStartupItem!.Checked));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_sizeItem);
        menu.Items.Add(_hotkeyItem);
        menu.Items.Add(_showOnStartupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Game Music Flyout",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? ToggleRequested;
    public event EventHandler<double>? ScaleRequested;
    public event EventHandler? ChangeHotkeyRequested;
    public event EventHandler<bool>? ShowOnStartupToggled;
    public event EventHandler? ExitRequested;

    /// <summary>Refreshes menu text and check marks.</summary>
    public void Update(bool isShown, double scale, Hotkey? hotkey, bool showOnStartup)
    {
        _toggleItem.Text = isShown ? "Hide flyout" : "Show flyout";
        foreach (var item in _sizeItem.DropDownItems.OfType<ToolStripMenuItem>())
        {
            item.Checked = item.Tag is double preset && Math.Abs(preset - scale) < 0.001;
        }

        var hotkeyText = hotkey?.DisplayText ?? "none";
        _hotkeyItem.Text = $"Shortcut: {hotkeyText}…";
        _showOnStartupItem.Checked = showOnStartup;

        // NotifyIcon.Text is limited to 127 characters.
        _notifyIcon.Text = hotkey is null ? "Game Music Flyout" : $"Game Music Flyout ({hotkeyText})";
    }

    public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info) =>
        _notifyIcon.ShowBalloonTip(5000, title, text, icon);

    public void Dispose()
    {
        // Hide first, or a ghost icon lingers in the tray until the mouse passes over it.
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}

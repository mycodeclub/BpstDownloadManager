using System;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace VeloxDM.App.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action open, Action addFromClipboard, Action pauseAll, Action exit)
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/velox.ico"))!.Stream;
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open VeloxDM", null, (_, _) => open());
        menu.Items.Add("Add URL from clipboard", null, (_, _) => addFromClipboard());
        menu.Items.Add("Pause all", null, (_, _) => pauseAll());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            Text = "VeloxDM",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => open();
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke();
    }

    public event Action? BalloonClicked;

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.Info);

    public void SetTooltip(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

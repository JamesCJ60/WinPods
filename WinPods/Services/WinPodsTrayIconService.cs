using System.Drawing;
using Forms = System.Windows.Forms;

namespace WinPods.Services;

public sealed class WinPodsTrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public event EventHandler? LeftClicked;
    public event EventHandler? Hovered;
    public event EventHandler? OpenRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? ExitRequested;

    public WinPodsTrayIconService()
    {
        _icon = LoadIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "WinPods",
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseMove += OnMouseMove;
    }

    public bool Register()
    {
        if (_disposed) return false;

        try
        {
            _notifyIcon.Visible = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Refresh()
    {
        if (_disposed || !_notifyIcon.Visible) return;
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.MouseMove -= OnMouseMove;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 10f)
        };
        menu.Items.Add("Open WinPods", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh AirPods", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            LeftClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseMove(object? sender, Forms.MouseEventArgs e) => Hovered?.Invoke(this, EventArgs.Empty);

    private static Icon LoadIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/Icon/app-icon.ico"));
        if (resource is null) return SystemIcons.Application;
        using var stream = resource.Stream;
        return new Icon(stream);
    }
}

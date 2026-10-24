using System.IO;
using System.Windows;
using System.Windows.Threading;
using CleanSlate.Presentation.Infrastructure;
using Forms = System.Windows.Forms;

namespace CleanSlate.App.Services;

/// <summary>Notification-area icon: optional "minimize to tray" and completion notifications.</summary>
public sealed class TrayService : INotificationService, IDisposable
{
    private readonly Func<MainWindow?> _window;
    private readonly Func<bool> _alwaysVisible;
    private Forms.NotifyIcon? _icon;
    private DispatcherTimer? _hideTimer;

    public TrayService(Func<MainWindow?> window, Func<bool> alwaysVisible)
    {
        _window = window;
        _alwaysVisible = alwaysVisible;
    }

    public void Refresh()
    {
        if (_alwaysVisible())
        {
            EnsureIcon().Visible = true;
        }
        else if (_icon is not null && _hideTimer is null)
        {
            _icon.Visible = false;
        }
    }

    public void NotifyCompleted(string title, string message)
    {
        var window = _window();
        if (window is { IsVisible: true, IsActive: true, WindowState: not WindowState.Minimized })
        {
            return;
        }

        var icon = EnsureIcon();
        icon.Visible = true;
        icon.ShowBalloonTip(6000, title, message.Length > 250 ? message[..250] : message, Forms.ToolTipIcon.Info);

        if (!_alwaysVisible())
        {
            _hideTimer?.Stop();
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer?.Stop();
                _hideTimer = null;
                if (!_alwaysVisible() && _icon is not null)
                {
                    _icon.Visible = false;
                }
            };
            _hideTimer.Start();
        }
    }

    private Forms.NotifyIcon EnsureIcon()
    {
        if (_icon is not null)
        {
            return _icon;
        }

        System.Drawing.Icon? appIcon = null;
        try
        {
            var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CleanSlate.ico"))?.Stream;
            if (stream is not null)
            {
                appIcon = new System.Drawing.Icon(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Fall back to the default icon.
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open CleanSlate", null, (_, _) => Restore());
        menu.Items.Add("Exit", null, (_, _) => _window()?.Close());

        _icon = new Forms.NotifyIcon
        {
            Icon = appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "CleanSlate",
            ContextMenuStrip = menu,
            Visible = false
        };
        _icon.DoubleClick += (_, _) => Restore();
        _icon.BalloonTipClicked += (_, _) => Restore();
        return _icon;
    }

    private void Restore() => _window()?.BringToFront();

    public void Dispose()
    {
        _hideTimer?.Stop();
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}

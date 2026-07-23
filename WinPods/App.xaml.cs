using System.Windows;
using System.Windows.Threading;
using WinPods.Services;

namespace WinPods;

public partial class App : System.Windows.Application
{
    private MainWindow? _mainWindow;
    private TraySummaryWindow? _summaryWindow;
    private WinPodsTrayIconService? _trayIcon;
    private DispatcherTimer? _hoverTimer;
    private DispatcherTimer? _trayPromotionTimer;
    private Rect _lastTrayBounds = Rect.Empty;
    private int _outsideTicks;
    private DateTimeOffset _casePromptUntil;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mainWindow = new MainWindow();
        _summaryWindow = new TraySummaryWindow(_mainWindow.ViewModel);
        _summaryWindow.OpenRequested += OnSummaryOpenRequested;
        _mainWindow.ViewModel.NearbyCaseOpened += OnNearbyCaseOpened;
        _mainWindow.ViewModel.NearbyPairingDetected += OnNearbyPairingDetected;
        _mainWindow.ViewModel.DeviceConnected += OnDeviceConnected;

        _trayIcon = new WinPodsTrayIconService();
        _trayIcon.LeftClicked += OnTrayLeftClicked;
        _trayIcon.Hovered += OnTrayHovered;
        _trayIcon.OpenRequested += OnTrayOpenRequested;
        _trayIcon.RefreshRequested += OnTrayRefreshRequested;
        _trayIcon.ExitRequested += OnTrayExitRequested;
        var trayRegistered = _trayIcon.Register();

        var promotionAttempts = 0;
        _trayPromotionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trayPromotionTimer.Tick += (_, _) =>
        {
            promotionAttempts++;
            if (TrayIconPromotionService.TryPromoteWinPods())
            {
                _trayIcon?.Refresh();
                _trayPromotionTimer.Stop();
            }
            else if (promotionAttempts >= 10)
                _trayPromotionTimer.Stop();
        };
        _trayPromotionTimer.Start();

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
        _hoverTimer.Tick += OnHoverTimerTick;
        _hoverTimer.Start();

        await _mainWindow.StartAsync();
        if (e.Args.Any(argument => string.Equals(argument, "--show-summary", StringComparison.OrdinalIgnoreCase)))
        {
            _casePromptUntil = DateTimeOffset.UtcNow.AddSeconds(30);
            _mainWindow.ViewModel.PrepareManualSummary();
            ShowSummary();
        }
        else if (!trayRegistered || e.Args.Any(argument => string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
            ShowMainWindow();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hoverTimer is not null)
        {
            _hoverTimer.Stop();
            _hoverTimer.Tick -= OnHoverTimerTick;
        }
        _trayPromotionTimer?.Stop();
        if (_trayIcon is not null)
        {
            _trayIcon.LeftClicked -= OnTrayLeftClicked;
            _trayIcon.Hovered -= OnTrayHovered;
            _trayIcon.OpenRequested -= OnTrayOpenRequested;
            _trayIcon.RefreshRequested -= OnTrayRefreshRequested;
            _trayIcon.ExitRequested -= OnTrayExitRequested;
            _trayIcon.Dispose();
        }
        if (_summaryWindow is not null)
        {
            _summaryWindow.OpenRequested -= OnSummaryOpenRequested;
            _summaryWindow.ClosePermanently();
        }
        if (_mainWindow is not null)
        {
            _mainWindow.ViewModel.NearbyCaseOpened -= OnNearbyCaseOpened;
            _mainWindow.ViewModel.NearbyPairingDetected -= OnNearbyPairingDetected;
            _mainWindow.ViewModel.DeviceConnected -= OnDeviceConnected;
            _mainWindow.ClosePermanently();
        }
        base.OnExit(e);
    }

    private void OnTrayLeftClicked(object? sender, EventArgs e)
    {
        if (_mainWindow?.IsVisible == true)
        {
            _mainWindow.Hide();
            return;
        }
        if (_mainWindow is not null &&
            DateTimeOffset.UtcNow - _mainWindow.LastHiddenAt < TimeSpan.FromMilliseconds(300))
            return;
        ShowMainWindow();
    }

    private void OnTrayHovered(object? sender, EventArgs e)
    {
        if (_summaryWindow?.IsVisible == true || _mainWindow?.IsVisible == true) return;
        if (_mainWindow?.ViewModel.ShowConnectedInSummary() != true) return;
        var cursor = TrayIconBounds.GetCursorPosition();
        _lastTrayBounds = new Rect(cursor.X - 16, cursor.Y - 16, 32, 32);
        _outsideTicks = 0;
        ShowSummary();
    }

    private void OnTrayOpenRequested(object? sender, EventArgs e) => ShowMainWindow();

    private async void OnTrayRefreshRequested(object? sender, EventArgs e)
    {
        if (_mainWindow is not null) await _mainWindow.ViewModel.RefreshAsync();
    }

    private void OnTrayExitRequested(object? sender, EventArgs e) => Shutdown();

    private void OnSummaryOpenRequested(object? sender, EventArgs e)
    {
        _summaryWindow?.HideSummary();
        ShowMainWindow();
    }

    private void OnNearbyCaseOpened(object? sender, EventArgs e)
    {
        if (UserNotificationState.ShouldSuppressAutomaticPopup()) return;
        ExtendSummaryVisibility(TimeSpan.FromSeconds(12));
        ShowSummary();
    }

    private void OnDeviceConnected(object? sender, EventArgs e)
    {
        if (UserNotificationState.ShouldSuppressAutomaticPopup()) return;
        ExtendSummaryVisibility(TimeSpan.FromSeconds(8));
        ShowSummary();
    }

    private void OnNearbyPairingDetected(object? sender, EventArgs e)
    {
        if (UserNotificationState.ShouldSuppressAutomaticPopup()) return;
        ExtendSummaryVisibility(TimeSpan.FromSeconds(12));
        ShowSummary();
    }

    private void ExtendSummaryVisibility(TimeSpan duration)
    {
        var requestedUntil = DateTimeOffset.UtcNow.Add(duration);
        if (requestedUntil > _casePromptUntil) _casePromptUntil = requestedUntil;
    }

    private void ShowMainWindow()
    {
        _summaryWindow?.HideSummary();
        _mainWindow?.ShowMain();
    }

    private void ShowSummary()
    {
        if (_summaryWindow is null || _mainWindow?.IsVisible == true) return;
        if (_lastTrayBounds.IsEmpty || _lastTrayBounds.Width <= 0 || _lastTrayBounds.Height <= 0)
        {
            var workArea = SystemParameters.WorkArea;
            _lastTrayBounds = new Rect(workArea.Right - 24, workArea.Bottom, 20, 20);
        }
        _summaryWindow.ShowNear(_lastTrayBounds);
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        if (_summaryWindow?.IsVisible != true) return;
        if (DateTimeOffset.UtcNow < _casePromptUntil) return;

        var cursor = TrayIconBounds.GetCursorPosition();
        var overTray = !_lastTrayBounds.IsEmpty && _lastTrayBounds.Contains(cursor);
        if (overTray || _summaryWindow.IsMouseOver)
        {
            _outsideTicks = 0;
            return;
        }

        if (++_outsideTicks >= 6)
        {
            _outsideTicks = 0;
            _summaryWindow.HideSummary();
        }
    }

}

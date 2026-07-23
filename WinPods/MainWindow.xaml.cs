using System.ComponentModel;
using System.Windows;
using WinPods.ViewModels;
using Wpf.Ui.Controls;

namespace WinPods;

public partial class MainWindow : FluentWindow
{
    private bool _initialised;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        Closing += OnClosing;
        Deactivated += OnDeactivated;
    }

    public MainViewModel ViewModel { get; }

    public DateTimeOffset LastHiddenAt { get; private set; }

    public async Task StartAsync()
    {
        if (_initialised) return;
        _initialised = true;
        await ViewModel.InitialiseAsync();
    }

    public void ShowMain()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        ViewModel.Dispose();
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_allowClose) return;
        LastHiddenAt = DateTimeOffset.UtcNow;
        Hide();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();

    private async void PairSwitch_Click(object sender, RoutedEventArgs e) => await ViewModel.PairOrSwitchAsync();

    private async void OpenPairing_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    private void RecommendedEq_Click(object sender, RoutedEventArgs e) => ViewModel.UseRecommendedEqualiser();

    private void CustomEq_Click(object sender, RoutedEventArgs e) => ViewModel.UseCustomEqualiser();

    private void Equaliser_EditCompleted(object? sender, EventArgs e) => ViewModel.SaveEqualiser();
}

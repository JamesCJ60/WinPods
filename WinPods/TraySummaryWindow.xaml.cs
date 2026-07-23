using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WinPods.ViewModels;

namespace WinPods;

public partial class TraySummaryWindow : Window
{
    private bool _allowClose;

    public TraySummaryWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += OnClosing;
    }

    public event EventHandler? OpenRequested;

    public void ShowNear(Rect trayBounds)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 12;
        ShowActivated = false;
        Show();
    }

    public void HideSummary() => Hide();

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void SummaryCard_Click(object sender, MouseButtonEventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private async void PairNearby_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel) await viewModel.PairNearbyAsync();
    }

    private void DismissNearby_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel) viewModel.DismissNearbyPairing();
        HideSummary();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }
}

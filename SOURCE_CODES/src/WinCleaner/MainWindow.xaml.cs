using System.Windows;
using WinCleaner.Services;
using WinCleaner.ViewModels;
using Wpf.Ui.Controls;

namespace WinCleaner;

public partial class MainWindow : FluentWindow
{
    private bool _startupRestoreStarted;

    public MainWindow()
    {
        InitializeComponent();

        if (DataContext is MainViewModel vm)
            ThemeController.Apply(vm.App.Settings.Theme);

        ContentRendered += OnContentRendered;
    }

    public void SetBusyOverlay(bool visible)
    {
        BusyOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
            BusyOverlay.Focus();
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        if (_startupRestoreStarted) return;
        _startupRestoreStarted = true;

        if (DataContext is MainViewModel vm)
            await vm.EnsureStartupRestorePointAsync(this).ConfigureAwait(true);
    }
}

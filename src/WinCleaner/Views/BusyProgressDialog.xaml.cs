using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace WinCleaner.Views;

/// <summary>
/// Modal busy dialog with progress bar for background work that would otherwise look frozen.
/// </summary>
public partial class BusyProgressDialog : FluentWindow
{
    private const int GwlStyle = -16;
    private const int WsSysmenu = 0x80000;
    private const int WsMinimizebox = 0x20000;
    private const int WsMaximizebox = 0x10000;

    private readonly Func<IProgress<BusyProgressUpdate>, CancellationToken, Task> _work;
    private bool _allowClose;
    private bool _lockPosition;
    private double _fixedLeft;
    private double _fixedTop;
    private Exception? _error;

    public Exception? Error => _error;

    public BusyProgressDialog(
        Func<IProgress<BusyProgressUpdate>, CancellationToken, Task> work,
        string? title = null,
        string? message = null,
        string? hint = null)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(title))
            TitleText.Text = title;
        if (!string.IsNullOrWhiteSpace(message))
            MessageText.Text = message;
        if (!string.IsNullOrWhiteSpace(hint))
            HintText.Text = hint;

        Closing += (_, e) =>
        {
            if (!_allowClose)
                e.Cancel = true;
        };

        SourceInitialized += OnSourceInitialized;
        LocationChanged += OnLocationChanged;
        Loaded += OnLoaded;
    }

    /// <summary>Shows a modal busy dialog, runs <paramref name="work"/>, then closes.</summary>
    public static void Run(
        Window? owner,
        string title,
        string message,
        Func<IProgress<BusyProgressUpdate>, CancellationToken, Task> work,
        string? hint = null)
    {
        var dialog = new BusyProgressDialog(work, title, message, hint) { Owner = owner };
        dialog.ShowDialog();
        if (dialog.Error is not null)
            throw dialog.Error;
    }

    /// <summary>Shows a modal busy dialog and returns the work result.</summary>
    public static T Run<T>(
        Window? owner,
        string title,
        string message,
        Func<IProgress<BusyProgressUpdate>, CancellationToken, Task<T>> work,
        string? hint = null)
    {
        T? result = default;
        var dialog = new BusyProgressDialog(async (progress, ct) =>
        {
            result = await work(progress, ct).ConfigureAwait(true);
        }, title, message, hint)
        {
            Owner = owner
        };
        dialog.ShowDialog();
        if (dialog.Error is not null)
            throw dialog.Error;
        return result!;
    }

    public void Report(BusyProgressUpdate update)
    {
        if (Dispatcher.CheckAccess())
            ApplyUpdate(update);
        else
            Dispatcher.BeginInvoke(() => ApplyUpdate(update), DispatcherPriority.Background);
    }

    private void ApplyUpdate(BusyProgressUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.Message))
            MessageText.Text = update.Message;

        if (update.Detail is not null)
            DetailText.Text = update.Detail;

        if (update.Percent is { } p)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = Math.Clamp(p, 0, 100);
        }
        else if (update.Indeterminate)
        {
            ProgressBar.IsIndeterminate = true;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var style = GetWindowLong(hwnd, GwlStyle);
        style &= ~(WsSysmenu | WsMinimizebox | WsMaximizebox);
        _ = SetWindowLong(hwnd, GwlStyle, style);
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_lockPosition) return;
        if (Math.Abs(Left - _fixedLeft) > 0.5 || Math.Abs(Top - _fixedTop) > 0.5)
        {
            Left = _fixedLeft;
            Top = _fixedTop;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        _fixedLeft = Left;
        _fixedTop = Top;
        _lockPosition = true;

        Topmost = true;
        Activate();
        Focus();

        var progress = new Progress<BusyProgressUpdate>(Report);
        using var cancel = new CancellationTokenSource();
        try
        {
            await _work(progress, cancel.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally
        {
            _allowClose = true;
            Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

public readonly record struct BusyProgressUpdate(
    string? Message = null,
    string? Detail = null,
    double? Percent = null,
    bool Indeterminate = false);

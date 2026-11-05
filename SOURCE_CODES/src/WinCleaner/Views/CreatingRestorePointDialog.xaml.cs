using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using WinCleaner.Core.Services;
using Wpf.Ui.Controls;

namespace WinCleaner.Views;

public partial class CreatingRestorePointDialog : FluentWindow
{
    private const int GwlStyle = -16;
    private const int WsSysmenu = 0x80000;
    private const int WsMinimizebox = 0x20000;
    private const int WsMaximizebox = 0x10000;

    private readonly Func<CancellationToken, Task<bool>> _work;
    private bool _allowClose;
    private bool _lockPosition;
    private double _fixedLeft;
    private double _fixedTop;

    public bool Succeeded { get; private set; }

    public CreatingRestorePointDialog(
        Func<CancellationToken, Task<bool>> work,
        string? message = null,
        string? hint = null)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
        InitializeComponent();

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
        StartPulse();

        using var cancel = new CancellationTokenSource();
        try
        {
            Succeeded = await _work(cancel.Token).ConfigureAwait(true);
            MessageText.Text = Succeeded
                ? (TryFindResource("restore.created") as string ?? "Restore point created.")
                : RestorePointManager.LastOutcome == RestorePointOutcome.ProtectionDisabled
                    ? (TryFindResource("gate.protectionOff") as string ?? "System Protection is turned off, so a restore point cannot be created.")
                    : (TryFindResource("restore.failed") as string ?? "Could not create a restore point.");
            HintText.Text = "";
            await Task.Delay(Succeeded ? 700 : 1400).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Succeeded = false;
            MessageText.Text = ex.Message;
            HintText.Text = "";
            await Task.Delay(1400).ConfigureAwait(true);
        }
        finally
        {
            _allowClose = true;
            Topmost = false;
            try { DialogResult = Succeeded; } catch { /* non-modal fallback */ }
            Close();
        }
    }

    private void StartPulse()
    {
        var scale = new DoubleAnimation
        {
            From = 1,
            To = 1.35,
            Duration = TimeSpan.FromSeconds(1.1),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var fade = new DoubleAnimation
        {
            From = 0.45,
            To = 0.1,
            Duration = TimeSpan.FromSeconds(1.1),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        PulseScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        PulseScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        PulseRing.BeginAnimation(OpacityProperty, fade);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}

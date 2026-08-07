using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinCleaner.Views;

/// <summary>
/// Live log window for long-running DISM/SFC (and similar) console tools.
/// </summary>
public sealed class CommandProgressWindow : Window
{
    private readonly TextBox _log;
    private readonly Button _closeButton;
    private readonly TextBlock _status;
    private readonly StringBuilder _buffer = new();
    private bool _completed;

    public CommandProgressWindow(string title)
    {
        Title = title;
        Width = 720;
        Height = 480;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TryFindResource("BgPanelBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));
        Foreground = TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.WhiteSmoke;

        var root = new DockPanel { Margin = new Thickness(16) };

        _status = new TextBlock
        {
            Text = "Running…",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold
        };
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        _closeButton = new Button
        {
            Content = "Close",
            IsEnabled = false,
            Margin = new Thickness(0, 10, 0, 0),
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _closeButton.Click += (_, _) => Close();
        if (TryFindResource("SecondaryButtonStyle") is Style secondary)
            _closeButton.Style = secondary;
        DockPanel.SetDock(_closeButton, Dock.Bottom);
        root.Children.Add(_closeButton);

        _log = new TextBox
        {
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = TryFindResource("BgDeepBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14)),
            Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gainsboro,
            BorderThickness = new Thickness(1),
            BorderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.DimGray,
            AcceptsReturn = true
        };
        root.Children.Add(_log);

        Content = root;
        Closing += (_, e) =>
        {
            if (!_completed)
                e.Cancel = true;
        };
    }

    public void AppendLine(string line)
    {
        if (Dispatcher.CheckAccess())
            AppendCore(line);
        else
            Dispatcher.BeginInvoke(() => AppendCore(line), DispatcherPriority.Background);
    }

    public void MarkCompleted(bool success, string? statusText = null)
    {
        void Done()
        {
            _completed = true;
            _status.Text = statusText
                ?? (success ? "Completed." : "Finished with errors.");
            _closeButton.IsEnabled = true;
            _closeButton.Content = "Close";
        }

        if (Dispatcher.CheckAccess()) Done();
        else Dispatcher.Invoke(Done);
    }

    private void AppendCore(string line)
    {
        _buffer.AppendLine(line);
        _log.AppendText(line + Environment.NewLine);
        _log.CaretIndex = _log.Text.Length;
        _log.ScrollToEnd();
    }
}

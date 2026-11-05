using System.Windows;
using System.Windows.Controls;
using WinCleaner.Core.Operations;

namespace WinCleaner.Views;

public sealed class OperationProgressWindow : Window
{
    private readonly TextBox _log;
    private readonly TextBlock _status;
    private readonly Button _closeButton;
    private readonly CancellationTokenSource _cts = new();
    private bool _finished;

    public OperationProgressWindow(string title)
    {
        Title = title;
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Black;
        var root = new DockPanel { Margin = new Thickness(12) };
        _status = new TextBlock
        {
            Foreground = System.Windows.Media.Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Text = title
        };
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            Background = System.Windows.Media.Brushes.Black,
            Foreground = System.Windows.Media.Brushes.Gainsboro
        };
        root.Children.Add(_log);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom);
        _closeButton = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 8, 8, 0), IsCancel = true };
        _closeButton.Click += (_, _) =>
        {
            if (_finished)
                Close();
            else
                _cts.Cancel();
        };
        var copy = new Button { Content = "Copy log", MinWidth = 90, Margin = new Thickness(0, 8, 0, 0) };
        copy.Click += (_, _) => Clipboard.SetText(_log.Text);
        buttons.Children.Add(_closeButton);
        buttons.Children.Add(copy);
        root.Children.Add(buttons);
        Content = root;
        Closing += (_, e) =>
        {
            if (!_finished)
            {
                e.Cancel = true;
                _cts.Cancel();
            }
        };
    }

    public CancellationToken Token => _cts.Token;

    public void Report(OperationStep step)
    {
        Dispatcher.Invoke(() =>
        {
            _status.Text = step.Message;
            _log.AppendText(step.Message + Environment.NewLine);
            _log.ScrollToEnd();
        });
    }

    public void MarkCompleted()
    {
        Dispatcher.Invoke(() =>
        {
            _finished = true;
            _closeButton.Content = "Close";
        });
    }
}

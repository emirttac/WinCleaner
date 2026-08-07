using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinCleaner.Converters;

/// <summary>
/// Two-way: bool IsChecked ↔ SelectedNav string equals ConverterParameter.
/// Avoids RadioButton.Command re-entrancy during visual tree swaps.
/// </summary>
public sealed class NavEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var current = value?.ToString() ?? "";
        var target = parameter?.ToString() ?? "";
        return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
            return parameter?.ToString() ?? Binding.DoNothing;
        return Binding.DoNothing;
    }
}

public static class UiThread
{
    public static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    public static Task RunAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return dispatcher.InvokeAsync(action).Task;
    }
}

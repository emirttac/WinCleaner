using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WinCleaner.Converters;

public sealed class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var risk = value is Core.Models.RiskLevel r ? r : Core.Models.RiskLevel.Safe;
            var key = risk switch
            {
                Core.Models.RiskLevel.Blocked => "TextMutedBrush",
                Core.Models.RiskLevel.Dangerous => "DangerBrush",
                Core.Models.RiskLevel.Caution => "WarningBrush",
                _ => "SuccessBrush"
            };
            return Application.Current?.TryFindResource(key) ?? System.Windows.Media.Brushes.Gray;
        }
        catch
        {
            return System.Windows.Media.Brushes.Gray;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString() == "Invert";
        bool flag;
        if (value is bool b) flag = b;
        else if (value is string s) flag = !string.IsNullOrWhiteSpace(s);
        else flag = value is not null;

        if (invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

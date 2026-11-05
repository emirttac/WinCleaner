using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinCleaner.Converters;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinCleaner.Services;

public static class ThemeController
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";

    private static bool _hooked;

    public static string CurrentMode { get; private set; } = Dark;

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        "light" => Light,
        "system" => System,
        _ => Dark
    };

    public static string Resolve(string mode)
    {
        if (!string.Equals(mode, System, StringComparison.Ordinal))
            return mode;
        return AppsUseLightTheme() ? Light : Dark;
    }

    public static void Apply(string? mode)
    {
        CurrentMode = Normalize(mode);
        ApplyResolved(Resolve(CurrentMode));
        EnsureHook();
    }

    private static void ApplyResolved(string resolved)
    {
        var wpfTheme = resolved == Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(wpfTheme, WindowBackdropType.Mica, updateAccent: false);
        ApplicationAccentColorManager.Apply(Color.FromRgb(0xE1, 0x06, 0x00), wpfTheme);
        SwapTokens(resolved);
    }

    private static void SwapTokens(string resolved)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var dictionaries = app.Resources.MergedDictionaries;
        ResourceDictionary? current = null;
        foreach (var dict in dictionaries)
        {
            if (dict.Contains("Theme.Kind"))
            {
                current = dict;
                break;
            }
        }

        if (current is not null && current["Theme.Kind"] as string == resolved)
            return;

        var source = resolved == Light
            ? new Uri("Themes/Light.xaml", UriKind.Relative)
            : new Uri("Themes/DarkRed.xaml", UriKind.Relative);
        var next = new ResourceDictionary { Source = source };
        if (current is null)
        {
            dictionaries.Insert(0, next);
            return;
        }

        var index = dictionaries.IndexOf(current);
        dictionaries.RemoveAt(index);
        dictionaries.Insert(index, next);
    }

    private static bool AppsUseLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int flag && flag == 1;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureHook()
    {
        if (_hooked)
            return;
        _hooked = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
                return;
            if (!string.Equals(CurrentMode, System, StringComparison.Ordinal))
                return;
            var resolved = Resolve(System);
            UiThread.Run(() => ApplyResolved(resolved));
        };
    }
}

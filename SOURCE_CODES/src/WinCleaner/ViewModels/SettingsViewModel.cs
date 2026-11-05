using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Compatibility;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Converters;
using WinCleaner.Data;
using WinCleaner.Services;
using WinCleaner.Views;

namespace WinCleaner.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly MainViewModel _main;
    private readonly UpdateService _updates = new();
    private int _updateCheckGate;

    public SettingsViewModel(AppServices app, LocalizationService loc, MainViewModel main)
    {
        _app = app;
        _loc = loc;
        _main = main;
        _autoRestorePoint = app.Settings.AutoRestorePoint;
        _allowDangerous = app.Settings.AllowDangerousActions;
        _checkUpdates = app.Settings.CheckUpdatesOnStartup;
        _logFolder = app.Settings.GetLogFolder();

        Languages = new ObservableCollection<LanguageOption>(
            LocalizationService.SupportedLanguageCodes.Select(code =>
                new LanguageOption(code, loc.Get($"set.lang.{code}"))));

        var current = LocalizationService.NormalizeLanguageCode(app.Settings.Language);
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == current) ?? Languages[0];
        Themes = new ObservableCollection<ThemeOption>(new[]
        {
            new ThemeOption(ThemeController.Dark, loc.Get("set.theme.dark")),
            new ThemeOption(ThemeController.Light, loc.Get("set.theme.light")),
            new ThemeOption(ThemeController.System, loc.Get("set.theme.system"))
        });
        var theme = ThemeController.Normalize(app.Settings.Theme);
        _selectedTheme = Themes.FirstOrDefault(t => t.Code == theme) ?? Themes[0];
        RefreshLabels();
    }

    public ObservableCollection<LanguageOption> Languages { get; }
    public ObservableCollection<ThemeOption> Themes { get; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private bool _autoRestorePoint;
    [ObservableProperty] private bool _allowDangerous;
    [ObservableProperty] private bool _checkUpdates;
    [ObservableProperty] private string _logFolder;

    public void RefreshLabels()
    {
        Title = _loc.Get("set.title");
        Subtitle = _loc.Get("set.subtitle");
        foreach (var opt in Languages)
            opt.DisplayName = _loc.Get($"set.lang.{opt.Code}");
        foreach (var theme in Themes)
            theme.DisplayName = _loc.Get($"set.theme.{theme.Code.ToLowerInvariant()}");
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;
        var lang = LocalizationService.NormalizeLanguageCode(value.Code);
        if (string.Equals(_app.Settings.Language, lang, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_loc.CurrentLanguage, lang, StringComparison.OrdinalIgnoreCase))
            return;

        _app.Settings.Language = lang;
        _app.Settings.Save();
        _loc.SetLanguage(lang);
        _app.SetLocalizer(k => _loc.Get(k));
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null) return;
        var theme = ThemeController.Normalize(value.Code);
        if (string.Equals(ThemeController.Normalize(_app.Settings.Theme), theme, StringComparison.Ordinal) &&
            string.Equals(ThemeController.CurrentMode, theme, StringComparison.Ordinal))
            return;

        _app.Settings.Theme = theme;
        _app.Settings.Save();
        ThemeController.Apply(theme);
    }

    partial void OnAutoRestorePointChanged(bool value)
    {
        _app.Settings.AutoRestorePoint = value;
        _app.Settings.Save();
    }

    partial void OnAllowDangerousChanged(bool value)
    {
        _app.Settings.AllowDangerousActions = value;
        _app.Settings.Save();
    }

    partial void OnCheckUpdatesChanged(bool value)
    {
        _app.Settings.CheckUpdatesOnStartup = value;
        _app.Settings.Save();
        if (value)
            _ = CheckForUpdatesAsync(notifyWhenCurrent: true);
    }

    /// <summary>Called once after MainViewModel init when startup check is enabled.</summary>
    public Task CheckForUpdatesOnStartupAsync()
    {
        if (!_app.Settings.CheckUpdatesOnStartup)
            return Task.CompletedTask;
        return CheckForUpdatesAsync(notifyWhenCurrent: false);
    }

    private async Task CheckForUpdatesAsync(bool notifyWhenCurrent)
    {
        if (Interlocked.Exchange(ref _updateCheckGate, 1) == 1)
            return;

        try
        {
            var result = await _updates.CheckForUpdatesAsync().ConfigureAwait(true);
            await UiThread.RunAsync(() => PresentUpdateResult(result, notifyWhenCurrent)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Update check failed: {ex.Message}");
            await UiThread.RunAsync(() =>
            {
                MessageBox.Show(
                    string.Format(_loc.Get("set.updateError"), ex.Message),
                    _loc.Get("set.updateTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }).ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Exchange(ref _updateCheckGate, 0);
        }
    }

    private async Task PresentUpdateResult(UpdateCheckResult result, bool notifyWhenCurrent)
    {
        var title = _loc.Get("set.updateTitle");

        if (!result.Success)
        {
            // Silent on startup so a missing repo does not nag; notify when user toggles the option on.
            if (notifyWhenCurrent)
            {
                MessageBox.Show(
                    string.Format(_loc.Get("set.updateError"), result.ErrorMessage ?? "Unknown error"),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                AppLog.Info($"Startup update check skipped/failed: {result.ErrorMessage}");
            }
            return;
        }

        if (result.UpdateAvailable)
        {
            var body = string.Format(
                _loc.Get("set.updateAvailable"),
                result.LatestVersion,
                result.CurrentVersion);
            var answer = MessageBox.Show(body, title, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
                return;

            var downloaded = await _updates.DownloadVerifiedInstallerAsync(result).ConfigureAwait(true);
            if (!downloaded.CanRun || string.IsNullOrWhiteSpace(downloaded.Path))
            {
                MessageBox.Show(
                    _loc.Get(downloaded.Error ?? "set.updateNoHash"),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = downloaded.Path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        if (notifyWhenCurrent)
        {
            MessageBox.Show(
                string.Format(_loc.Get("set.updateCurrent"), result.CurrentVersion),
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void SaveLogFolder()
    {
        _app.Settings.LogFolder = LogFolder;
        _app.Settings.Save();
        Directory.CreateDirectory(LogFolder);
        MessageBox.Show(_loc.Get("common.save"), "WinCleaner");
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
        Process.Start(new ProcessStartInfo { FileName = LogFolder, UseShellExecute = true });
    }

    [RelayCommand]
    private void ExportChangeReport()
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            _app.ChangeLog.ExportSessionReport(asText: true);
            var path = _app.ChangeLog.LastExportPath;
            MessageBox.Show(
                string.Format(_loc.Get("set.exportReport.done"), path ?? LogFolder),
                _loc.Get("set.exportReport"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenSocial(string? network)
    {
        var url = network?.Trim().ToLowerInvariant() switch
        {
            "youtube" => "https://www.youtube.com/@BiAltTab",
            "instagram" => "https://www.instagram.com/emirttac/",
            "github" => "https://github.com/emirttac",
            _ => null
        };
        if (url is null) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

/// <summary>Display entry for the Settings language ComboBox.</summary>

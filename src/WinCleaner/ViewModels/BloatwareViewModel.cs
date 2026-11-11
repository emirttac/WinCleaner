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

public partial class BloatwareViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;
    private List<AppRowViewModel> _all = new();
    private string? _activeQuery;
    private bool _suppressFilter;
    private const string FilterInstalledId = "installed";
    private const string FilterAllId = "all";

    public BloatwareViewModel(
        AppServices app,
        LocalizationService loc,
        Func<Task<bool>> confirm,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
    {
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _previewApply = previewApply;
        RefreshLabels();
        _ = ReloadAsync();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _selectedFilterId = FilterInstalledId;
    [ObservableProperty] private string _emptyText = "";
    public ObservableCollection<AppRowViewModel> Items { get; } = new();
    public ObservableCollection<BloatwareFilterOption> FilterOptions { get; } = new();
    public bool CanRunBloatware => !IsLoading && !IsBusy;
    public bool ShowInstalledOnly => !string.Equals(SelectedFilterId, FilterAllId, StringComparison.Ordinal);
    public bool HasNoVisibleItems => !IsLoading && Items.Count == 0;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunBloatware));
        OnPropertyChanged(nameof(HasNoVisibleItems));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunBloatware));

    partial void OnSelectedFilterIdChanged(string value)
    {
        if (_suppressFilter) return;
        Filter(_activeQuery);
    }

    public void RefreshLabels()
    {
        Title = _loc.Get("blo.title");
        Subtitle = _loc.Get("blo.subtitle");
        RebuildFilterOptions();
        foreach (var item in _all)
            item.RefreshLabels(_loc);
        Filter(_activeQuery);
    }

    private void RebuildFilterOptions()
    {
        var selected = string.IsNullOrWhiteSpace(SelectedFilterId) ? FilterInstalledId : SelectedFilterId;
        _suppressFilter = true;
        try
        {
            FilterOptions.Clear();
            FilterOptions.Add(new BloatwareFilterOption(FilterInstalledId, _loc.Get("blo.filter.installed")));
            FilterOptions.Add(new BloatwareFilterOption(FilterAllId, _loc.Get("common.all")));
            SelectedFilterId = selected;
        }
        finally
        {
            _suppressFilter = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            AppLog.Info("Bloatware ReloadAsync start");
            var installed = await _app.Appx.GetInstalledPackagesAsync().ConfigureAwait(true);
            var names = new HashSet<string>(installed.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

            var rows = CatalogLoader.LoadApps()
                .Where(a => OsCompatibility.IsCompatible(null, a.Win11Only, _app.Os))
                .Select(a => new AppRowViewModel(a, a.MatchesAnyInstalled(names), _loc))
                .ToList();

            await UiThread.RunAsync(() =>
            {
                _all = rows;
                Filter(_activeQuery);
            }).ConfigureAwait(true);

            AppLog.Info($"Bloatware ReloadAsync done count={_all.Count}");
        }
        catch (Exception ex)
        {
            AppLog.Crash("Bloatware.ReloadAsync", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Filter(string? query)
    {
        _activeQuery = query;
        Items.Clear();
        IEnumerable<AppRowViewModel> rows = SearchAll(query);
        if (ShowInstalledOnly)
            rows = rows.Where(i => i.IsInstalled);
        else
            rows = rows.OrderByDescending(i => i.IsInstalled).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in rows)
            Items.Add(item);
        UpdateEmptyText();
        OnPropertyChanged(nameof(HasNoVisibleItems));
    }

    public IEnumerable<AppRowViewModel> SearchAll(string? query) =>
        _all.Where(i => i.MatchesQuery(query));

    private void UpdateEmptyText()
    {
        EmptyText = ShowInstalledOnly ? _loc.Get("blo.emptyInstalled") : "";
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected && i.IsInstalled && i.CanSelect).ToList();
        if (selected.Count == 0) return;

        if (MessageBox.Show(_loc.Get("blo.confirm"), "WinCleaner", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (selected.Any(s => s.Risk == RiskLevel.Dangerous) && !await _confirm())
            return;

        IsBusy = true;
        try
        {
            var actions = selected.Select(s => _app.ActionFactory.CreateAppxRemove(
                CatalogLoader.LoadApps().First(a => a.Id == s.Id))).Cast<IChangeAction>().ToList();
            if (_previewApply is not null)
            {
                await _previewApply(actions, _loc.Get("nav.bloatware")).ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
                return;
            }

            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("nav.bloatware"), token =>
                _app.RunActionsAsync(_loc.Get("nav.bloatware"), actions, _loc.Get("nav.bloatware"), token));
            if (!op.Success && !op.Cancelled && !string.IsNullOrWhiteSpace(op.Message))
            {
                MessageBox.Show(_loc.Get(op.Message), _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool EnsureDangerousAllowed()
    {
        if (_app.Settings.AllowDangerousActions)
            return true;
        MessageBox.Show(
            _loc.Get("danger.needSettings"),
            _loc.Get("danger.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    [RelayCommand]
    private async Task UninstallOneDriveAsync()
    {
        if (!EnsureDangerousAllowed())
            return;
        if (MessageBox.Show(_loc.Get("blo.onedriveConfirm"), _loc.Get("app.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (!await _confirm()) return;

        IsBusy = true;
        try
        {
            var action = _app.ActionFactory.CreateOneDriveUninstall();
            if (_previewApply is not null)
            {
                await _previewApply([action], _loc.Get("blo.onedrive")).ConfigureAwait(true);
                return;
            }

            var result = await OperationHost.TrackAsync(_app.Operations, _loc.Get("blo.onedrive"), token =>
                _app.RunActionAsync(_loc.Get("blo.onedrive"), action, false, token));
            if (!result.Success)
            {
                var detail = string.IsNullOrWhiteSpace(result.Message)
                    ? _loc.Get("blo.onedriveFailed")
                    : _loc.Get(result.Message);
                MessageBox.Show(detail, _loc.Get("app.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(result.Message ?? _loc.Get("blo.onedriveOk"), _loc.Get("app.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UninstallEdgeAsync()
    {
        if (!EnsureDangerousAllowed())
            return;
        if (MessageBox.Show(_loc.Get("blo.edgeConfirm"), _loc.Get("app.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (!await _confirm()) return;

        IsBusy = true;
        try
        {
            var action = _app.ActionFactory.CreateEdgeUninstall();
            if (_previewApply is not null)
            {
                await _previewApply([action], _loc.Get("blo.edge")).ConfigureAwait(true);
                return;
            }

            var result = await OperationHost.TrackAsync(_app.Operations, _loc.Get("blo.edge"), token =>
                _app.RunActionAsync(_loc.Get("blo.edge"), action, false, token));
            if (!result.Success)
            {
                MessageBox.Show(FormatEdgeFailure(result.Message), _loc.Get("app.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(result.Message ?? _loc.Get("blo.edgeOk"), _loc.Get("app.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FormatEdgeFailure(ex.Message), _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string FormatEdgeFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return _loc.Get("blo.edgeFailed");
        if (message.Contains("EDGE_STILL_INSTALLED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("still installed after uninstall", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("blo.edgeStillInstalled", StringComparison.OrdinalIgnoreCase))
            return _loc.Get("blo.edgeStillInstalled");

        var localized = _loc.Get(message);
        if (!string.Equals(localized, message, StringComparison.Ordinal))
            return localized;

        // Strip PowerShell exception wrappers from temp scripts.
        var marker = "Microsoft Edge is still installed";
        var idx = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return _loc.Get("blo.edgeStillInstalled");

        return message.Contains("At C:\\", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("CategoryInfo", StringComparison.OrdinalIgnoreCase)
            ? _loc.Get("blo.edgeFailed")
            : message;
    }

    [RelayCommand]
    private async Task ReinstallSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var catalog = CatalogLoader.LoadApps();
        var missingIds = selected
            .Where(s => !catalog.First(a => a.Id == s.Id).CanReinstall)
            .Select(s => s.Title)
            .ToList();
        if (missingIds.Count > 0)
        {
            MessageBox.Show(
                _loc.Format("blo.reinstallNoStoreId", string.Join(", ", missingIds.Take(5))),
                _loc.Get("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var entries = selected
            .Select(s => catalog.First(a => a.Id == s.Id))
            .Where(a => a.CanReinstall)
            .ToList();
        if (entries.Count == 0) return;

        if (MessageBox.Show(_loc.Get("blo.reinstallConfirm"), _loc.Get("app.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (entries.Any(e => RiskParser.Parse(e.Risk) == RiskLevel.Dangerous) && !await _confirm())
            return;

        IsBusy = true;
        try
        {
            var actions = new List<IChangeAction>();
            foreach (var entry in entries)
            {
                var action = _app.ActionFactory.CreateAppxInstall(entry);
                if (action is not null)
                    actions.Add(action);
            }

            if (actions.Count == 0) return;

            if (_previewApply is not null)
            {
                await _previewApply(actions, _loc.Get("blo.reinstall")).ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
                return;
            }

            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("blo.reinstall"), token =>
                _app.RunActionsAsync(_loc.Get("blo.reinstall"), actions, _loc.Get("blo.reinstall"), token));
            if (!op.Success && !op.Cancelled && !string.IsNullOrWhiteSpace(op.Message))
            {
                MessageBox.Show(_loc.Get(op.Message), _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(_loc.Get("blo.reinstallOk"), _loc.Get("app.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed record BloatwareFilterOption(string Id, string Label);

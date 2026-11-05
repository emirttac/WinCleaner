using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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

public partial class ServicesViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;
    private List<ServiceRowViewModel> _all = new();

    public ServicesViewModel(
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
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    public ObservableCollection<ServiceRowViewModel> Items { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    [ObservableProperty] private string? _selectedCategory;

    public void RefreshLabels()
    {
        Title = _loc.Get("svc.title");
        Subtitle = _loc.Get("svc.subtitle");
        var previousCategory = SelectedCategory;
        var allLabel = _loc.Get("common.all");
        // Rebuild category labels that use the localized "(All)" sentinel
        if (Categories.Count > 0)
        {
            Categories[0] = allLabel;
            if (previousCategory is null ||
                previousCategory.StartsWith('(') ||
                string.Equals(previousCategory, allLabel, StringComparison.Ordinal))
                SelectedCategory = allLabel;
        }
        foreach (var item in _all)
            item.RefreshLabels();
        Filter(null);
    }

    [RelayCommand]
    private void Reload()
    {
        // Service enumeration can be slow — keep UI responsive
        _ = ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            AppLog.Info("Services ReloadAsync start");
            var catalog = CatalogLoader.LoadServices()
                .Where(s => OsCompatibility.IsCompatible(s.MinBuild, s.Win11Only, _app.Os))
                .ToList();

            var live = await Task.Run(() =>
                _app.Services.GetAllServices()
                    .ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase))
                .ConfigureAwait(true);

            await UiThread.RunAsync(() =>
            {
                _all = catalog.Select(c =>
                {
                    var info = ServiceNameMatcher.MatchLive(live, c.GetServiceNames());
                    return new ServiceRowViewModel(c, info, _app, _loc, _confirm, _previewApply);
                }).ToList();

                Categories.Clear();
                Categories.Add(_loc.Get("common.all"));
                foreach (var cat in _all.Select(x => x.Category).Distinct().OrderBy(x => x))
                    Categories.Add(cat);

                SelectedCategory = _loc.Get("common.all");
                Filter(null);
            }).ConfigureAwait(true);

            AppLog.Info($"Services ReloadAsync done count={_all.Count}");
        }
        catch (Exception ex)
        {
            AppLog.Crash("Services.ReloadAsync", ex);
        }
    }

    private string? _activeQuery;

    public void Filter(string? query)
    {
        _activeQuery = query;
        Items.Clear();
        foreach (var item in _all)
        {
            if (SelectedCategory is not null &&
                !string.Equals(SelectedCategory, _loc.Get("common.all"), StringComparison.Ordinal) &&
                item.Category != SelectedCategory)
                continue;
            if (!item.MatchesSearch(query))
                continue;
            Items.Add(item);
        }
    }

    public IEnumerable<ServiceRowViewModel> SearchAll(string? query) =>
        _all.Where(item => item.MatchesSearch(query));

    partial void OnSelectedCategoryChanged(string? value) => Filter(_activeQuery);

    /// <summary>
    /// Export Disabled/Manual service states from the full (unfiltered) list.
    /// Automatic and blocked services are omitted.
    /// </summary>
    public IReadOnlyList<ServiceStateEntry> GetExportServiceStates()
    {
        var list = new List<ServiceStateEntry>();
        foreach (var row in _all)
        {
            if (!row.IsInstalled || row.Risk == RiskLevel.Blocked)
                continue;

            string? startType = null;
            if (row.IsDisabled)
                startType = "Disabled";
            else if (string.Equals(row.StartTypeText, "Manual", StringComparison.OrdinalIgnoreCase))
                startType = "Manual";

            if (startType is null)
                continue;

            list.Add(new ServiceStateEntry { Id = row.Id, StartType = startType });
        }

        return list
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [RelayCommand]
    private async Task DisableCategoryAsync()
    {
        if (SelectedCategory is null || SelectedCategory == _loc.Get("common.all")) return;
        var targets = _all.Where(x => x.Category == SelectedCategory && x.Risk != RiskLevel.Blocked).ToList();
        var actions = targets.Select(t => t.CreateDisableAction()).Cast<IChangeAction>().ToList();
        if (_previewApply is not null)
        {
            await _previewApply(actions, SelectedCategory).ConfigureAwait(true);
            Reload();
            return;
        }

        var preview = string.Join(Environment.NewLine, actions.Select(a => $"• {a.DisplayName}"));
        if (MessageBox.Show(
                preview + Environment.NewLine + Environment.NewLine + _loc.Get("msg.disableConfirm"),
                _loc.Get("app.title"),
                MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        if (actions.Any(a => a.Risk == RiskLevel.Dangerous) && !await _confirm()) return;
        await OperationHost.TrackAsync(_app.Operations, SelectedCategory ?? "", token =>
            _app.RunActionsAsync(SelectedCategory ?? "", actions, SelectedCategory, token));
        Reload();
    }
}

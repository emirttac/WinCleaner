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

public partial class TweaksCategoryViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<Task<bool>> _confirmAntiCheat;
    protected readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;
    private readonly string _category;
    private readonly string _titleKey;
    private readonly string _subtitleKey;
    private List<TweakItemViewModel> _all = new();

    public TweaksCategoryViewModel(
        string category, string titleKey, string subtitleKey,
        AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
    {
        _category = category;
        _titleKey = titleKey;
        _subtitleKey = subtitleKey;
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _confirmAntiCheat = confirmAntiCheat ?? confirm;
        _previewApply = previewApply;
        Tips = TipsText(loc);
        RefreshLabels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _tips = "";
    [ObservableProperty] private bool _isLoading;
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
    private int _probeGeneration;

    private string TipsText(LocalizationService loc) => _category.ToLowerInvariant() switch
    {
        "gaming" => loc.Get("gam.tips"),
        "telemetry" => loc.Get("tel.tips"),
        "windows11ui" => loc.Get("win11.tips"),
        _ => ""
    };

    public void RefreshLabels()
    {
        Title = _loc.Get(_titleKey);
        Subtitle = _loc.Get(_subtitleKey);
        Tips = TipsText(_loc);
        foreach (var item in _all)
            item.RefreshLabels();
    }

    [RelayCommand]
    private void Reload()
    {
        var generation = Interlocked.Increment(ref _probeGeneration);
        IsLoading = true;
        _all = CatalogLoader.LoadTweaks()
            .Where(t => t.Category.Equals(_category, StringComparison.OrdinalIgnoreCase))
            .Where(t => OsCompatibility.IsCompatible(t.MinBuild, t.Win11Only, _app.Os))
            .Select(CreateItem)
            .Where(x => x is not null)
            .Cast<TweakItemViewModel>()
            .ToList();
        Filter(null);
        _ = LoadAppliedStateAsync(generation);
    }

    private async Task LoadAppliedStateAsync(int generation)
    {
        try
        {
            await AppliedStateLoader.LoadAsync(
                _app,
                _all,
                generation,
                () => _probeGeneration,
                () => IsLoading = false).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Applied state load failed for {_category}: {ex.Message}");
            if (generation == _probeGeneration)
                IsLoading = false;
        }
    }

    private TweakItemViewModel? CreateItem(TweakCatalogEntry entry)
    {
        var action = _app.ActionFactory.CreateFromTweak(entry);
        if (action is null) return null;

        return new TweakItemViewModel(
            entry.Id,
            entry.DisplayNameKey,
            entry.DescriptionKey,
            entry.Category,
            RiskParser.Parse(entry.Risk),
            _app,
            _loc,
            () => _app.ActionFactory.CreateFromTweak(entry)!,
            isApplied: false,
            isEnabled: CatalogAccess.Allows(entry, _app.Os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture),
            confirmDangerous: _confirm,
            blockReasonKey: CatalogAccess.Allows(entry, _app.Os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture) ? null : GateCodes.Sku,
            requiresAntiCheatConfirm: entry.RequiresAntiCheatConfirm,
            confirmAntiCheat: _confirmAntiCheat,
            previewApply: _previewApply,
            stateKnown: false);
    }

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var item in SearchAll(query))
            Items.Add(item);
    }

    public IEnumerable<TweakItemViewModel> SearchAll(string? query) =>
        _all.Where(i => i.MatchesSearch(query));

    /// <summary>Applied tweak IDs from the full (unfiltered) catalog list.</summary>
    public IReadOnlyList<string> GetAppliedTweakIds() =>
        _all.Where(i => i.IsApplied).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

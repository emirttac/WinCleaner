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

public partial class ToolsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<Task<bool>> _confirmAntiCheat;
    private List<TweakItemViewModel> _all = new();

    private readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;

    public ToolsViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
    {
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _confirmAntiCheat = confirmAntiCheat ?? confirm;
        _previewApply = previewApply;
        _takeOwnershipEnabled = app.SystemHealth.IsTakeOwnershipMenuEnabled();
        RefreshLabels();
        RebuildLegacyPanels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _maintenanceTitle = "";
    [ObservableProperty] private string _tweaksSectionTitle = "";
    [ObservableProperty] private string _cleanTempLabel = "";
    [ObservableProperty] private string _createRestoreLabel = "";
    [ObservableProperty] private string _diskCleanupLabel = "";
    [ObservableProperty] private string _sfcDismLabel = "";
    [ObservableProperty] private string _winsxsLabel = "";
    [ObservableProperty] private string _godModeLabel = "";
    [ObservableProperty] private string _wuResetLabel = "";
    [ObservableProperty] private string _networkResetLabel = "";
    [ObservableProperty] private string _memoryDiagLabel = "";
    [ObservableProperty] private string _diskAnalyzeLabel = "";
    [ObservableProperty] private string _takeOwnTitle = "";
    [ObservableProperty] private string _takeOwnDescription = "";
    [ObservableProperty] private string _fixesTitle = "";
    [ObservableProperty] private string _legacyTitle = "";
    [ObservableProperty] private string _ntpLabel = "";
    [ObservableProperty] private string _autologonLabel = "";
    [ObservableProperty] private string _wingetRepairLabel = "";
    [ObservableProperty] private string _regExportLabel = "";
    [ObservableProperty] private string _regImportLabel = "";
    [ObservableProperty] private bool _takeOwnershipEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoading = true;
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
    public ObservableCollection<LegacyPanelItem> LegacyPanels { get; } = new();
    public bool CanRunTools => !IsBusy;
    private int _probeGeneration;

    public void RefreshLabels()
    {
        Title = _loc.Get("tools.title");
        Subtitle = _loc.Get("tools.subtitle");
        MaintenanceTitle = _loc.Get("tools.maintenance");
        TweaksSectionTitle = _loc.Get("tools.registrySafe");
        FixesTitle = _loc.Get("tools.fixes");
        LegacyTitle = _loc.Get("tools.legacy");
        NtpLabel = _loc.Get("tools.ntp");
        AutologonLabel = _loc.Get("tools.autologon");
        WingetRepairLabel = _loc.Get("tools.wingetRepair");
        RegExportLabel = _loc.Get("tools.regExport");
        RegImportLabel = _loc.Get("tools.regImport");
        CleanTempLabel = _loc.Get("tools.cleanTemp");
        CreateRestoreLabel = _loc.Get("dash.createRestore");
        DiskCleanupLabel = _loc.Get("tools.diskCleanup");
        SfcDismLabel = _loc.Get("tools.sfcDism");
        WinsxsLabel = _loc.Get("tools.winsxs");
        GodModeLabel = _loc.Get("tools.godMode");
        WuResetLabel = _loc.Get("tools.wuReset");
        NetworkResetLabel = _loc.Get("tools.networkReset");
        MemoryDiagLabel = _loc.Get("tools.memoryDiag");
        DiskAnalyzeLabel = _loc.Get("tools.diskAnalyze");
        TakeOwnTitle = _loc.Get("tools.takeOwn.name");
        TakeOwnDescription = _loc.Get("tools.takeOwn.desc");
        RebuildLegacyPanels();
        foreach (var item in _all)
            item.RefreshLabels();
    }

    private void RebuildLegacyPanels()
    {
        LegacyPanels.Clear();
        foreach (var p in LegacyPanelCatalog.All)
        {
            LegacyPanels.Add(new LegacyPanelItem
            {
                Id = p.Id,
                Target = p.Target,
                Title = _loc.Get(p.TitleKey)
            });
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunTools));

    private void Reload()
    {
        var generation = Interlocked.Increment(ref _probeGeneration);
        IsLoading = true;
        _all = CatalogLoader.LoadTweaks()
            .Where(t => t.Category.Equals("Tools", StringComparison.OrdinalIgnoreCase))
            .Where(t => OsCompatibility.IsCompatible(t.MinBuild, t.Win11Only, _app.Os))
            .Where(t => CatalogAccess.Allows(t, _app.Os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture))
            .Select(entry =>
            {
                return new TweakItemViewModel(
                    entry.Id, entry.DisplayNameKey, entry.DescriptionKey,
                    entry.Category, RiskParser.Parse(entry.Risk), _app, _loc,
                    () => _app.ActionFactory.CreateFromTweak(entry)!, false,
                    CatalogAccess.Allows(entry, _app.Os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture),
                    _confirm,
                    entry.RequiresAntiCheatConfirm, _confirmAntiCheat, _previewApply,
                    blockReasonKey: CatalogAccess.Allows(entry, _app.Os.Sku, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture) ? null : GateCodes.Sku,
                    stateKnown: false);
            }).ToList();
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
            AppLog.Error($"Applied state load failed for Tools: {ex.Message}");
            if (generation == _probeGeneration)
                IsLoading = false;
        }
    }

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var i in SearchAll(query))
            Items.Add(i);
    }

    public IEnumerable<TweakItemViewModel> SearchAll(string? query) =>
        _all.Where(x => x.MatchesSearch(query));

    public IReadOnlyList<string> GetAppliedTweakIds() =>
        _all.Where(i => i.IsApplied).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

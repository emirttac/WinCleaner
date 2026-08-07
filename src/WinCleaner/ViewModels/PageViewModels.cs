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
    private readonly string _category;
    private readonly string _titleKey;
    private readonly string _subtitleKey;
    private List<TweakItemViewModel> _all = new();

    public TweaksCategoryViewModel(
        string category, string titleKey, string subtitleKey,
        AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null)
    {
        _category = category;
        _titleKey = titleKey;
        _subtitleKey = subtitleKey;
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _confirmAntiCheat = confirmAntiCheat ?? confirm;
        Tips = loc.Get("common.comingSoon");
        RefreshLabels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _tips = "";
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();

    public void RefreshLabels()
    {
        Title = _loc.Get(_titleKey);
        Subtitle = _loc.Get(_subtitleKey);
        Tips = _loc.Get("common.comingSoon");
        foreach (var item in _all)
            item.RefreshLabels();
    }

    [RelayCommand]
    private void Reload()
    {
        _all = CatalogLoader.LoadTweaks()
            .Where(t => t.Category.Equals(_category, StringComparison.OrdinalIgnoreCase))
            .Where(t => OsCompatibility.IsCompatible(t.MinBuild, t.Win11Only, _app.Os))
            .Select(CreateItem)
            .Where(x => x is not null)
            .Cast<TweakItemViewModel>()
            .ToList();
        Filter(null);
    }

    private TweakItemViewModel? CreateItem(TweakCatalogEntry entry)
    {
        var action = _app.ActionFactory.CreateFromTweak(entry);
        if (action is null) return null;

        var applied = false;
        if (action is RegistryChangeAction reg)
            applied = reg.IsApplied();
        else if (action is MultiRegistryChangeAction multi)
            applied = multi.IsApplied();
        else if (action is CommandChangeAction cmd)
            applied = cmd.IsApplied();

        return new TweakItemViewModel(
            entry.Id,
            entry.DisplayNameKey,
            entry.DescriptionKey,
            entry.Category,
            RiskParser.Parse(entry.Risk),
            _app,
            _loc,
            () => _app.ActionFactory.CreateFromTweak(entry)!,
            isApplied: applied,
            confirmDangerous: _confirm,
            requiresAntiCheatConfirm: entry.RequiresAntiCheatConfirm,
            confirmAntiCheat: _confirmAntiCheat);
    }

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var item in _all.Where(i => i.MatchesSearch(query)))
            Items.Add(item);
    }

    /// <summary>Applied tweak IDs from the full (unfiltered) catalog list.</summary>
    public IReadOnlyList<string> GetAppliedTweakIds() =>
        _all.Where(i => i.IsApplied).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

public partial class BloatwareViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private List<AppRowViewModel> _all = new();

    public BloatwareViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm)
    {
        _app = app;
        _loc = loc;
        _confirm = confirm;
        RefreshLabels();
        _ = ReloadAsync();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private bool _isLoading;
    public ObservableCollection<AppRowViewModel> Items { get; } = new();

    public void RefreshLabels()
    {
        Title = _loc.Get("blo.title");
        Subtitle = _loc.Get("blo.subtitle");
        foreach (var item in _all)
            item.RefreshLabels(_loc);
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
                .Select(a => new AppRowViewModel(a, names.Contains(a.PackageName) || names.Any(n => n.StartsWith(a.PackageName, StringComparison.OrdinalIgnoreCase)), _loc))
                .ToList();

            await UiThread.RunAsync(() =>
            {
                _all = rows;
                Filter(null);
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
        Items.Clear();
        foreach (var item in _all)
        {
            if (!string.IsNullOrWhiteSpace(query) &&
                !item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !item.PackageName.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Items.Add(item);
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedAsync()
    {
        var selected = Items.Where(i => i.IsSelected && i.CanSelect).ToList();
        if (selected.Count == 0) return;

        if (MessageBox.Show(_loc.Get("blo.confirm"), "WinCleaner", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (selected.Any(s => s.Risk == RiskLevel.Dangerous) && !await _confirm())
            return;

        var actions = selected.Select(s => _app.ActionFactory.CreateAppxRemove(
            CatalogLoader.LoadApps().First(a => a.Id == s.Id))).Cast<IChangeAction>().ToList();
        var results = await _app.Executor.ExecuteManyAsync(actions);
        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count > 0)
        {
            var msg = string.Join(Environment.NewLine, failed.Select(f => f.Message).Where(m => !string.IsNullOrWhiteSpace(m)).Take(5));
            MessageBox.Show(
                string.IsNullOrWhiteSpace(msg)
                    ? _loc.Format("blo.removeFailedCount", failed.Count)
                    : msg,
                _loc.Get("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task UninstallOneDriveAsync()
    {
        if (MessageBox.Show(_loc.Get("blo.onedriveConfirm"), _loc.Get("app.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        if (!await _confirm()) return;

        var action = _app.ActionFactory.CreateOneDriveUninstall();
        var result = await _app.Executor.ExecuteAsync(action);
        if (!result.Success)
        {
            MessageBox.Show(result.Message ?? _loc.Get("blo.onedriveFailed"), _loc.Get("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(result.Message ?? _loc.Get("blo.onedriveOk"), _loc.Get("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

public partial class AppRowViewModel : ObservableObject
{
    private readonly AppCatalogEntry _entry;
    private readonly string _criticalKey;

    public AppRowViewModel(AppCatalogEntry entry, bool isInstalled, LocalizationService loc)
    {
        _entry = entry;
        _criticalKey = "blo.critical";
        Id = entry.Id;
        PackageName = entry.PackageName;
        Risk = RiskParser.Parse(entry.Risk);
        IsSystemCritical = entry.SystemCritical;
        IsInstalled = isInstalled;
        CanSelect = isInstalled && !entry.SystemCritical;
        RefreshLabels(loc);
    }

    public string Id { get; }
    public string PackageName { get; }
    public RiskLevel Risk { get; }
    public bool IsSystemCritical { get; }
    public bool IsInstalled { get; }
    public bool CanSelect { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _riskLabel = "";
    [ObservableProperty] private string _criticalLabel = "";
    [ObservableProperty] private bool _isSelected;

    public void RefreshLabels(LocalizationService loc)
    {
        Title = loc.Get(_entry.DisplayNameKey);
        Description = loc.Get(_entry.DescriptionKey);
        RiskLabel = Risk switch
        {
            RiskLevel.Blocked => loc.Get("common.risk.blocked"),
            RiskLevel.Dangerous => loc.Get("common.risk.dangerous"),
            RiskLevel.Caution => loc.Get("common.risk.caution"),
            _ => loc.Get("common.risk.safe")
        };
        CriticalLabel = IsSystemCritical ? loc.Get(_criticalKey) : "";
    }
}

public partial class VisualViewModel : TweaksCategoryViewModel
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;

    public VisualViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null)
        : base("Visual", "vis.title", "vis.subtitle", app, loc, confirm, confirmAntiCheat)
    {
        _app = app;
        _loc = loc;
        ReloadStartup();
    }

    public ObservableCollection<StartupRowViewModel> StartupItems { get; } = new();
    [ObservableProperty] private string _startupTitle = "";

    public new void RefreshLabels()
    {
        base.RefreshLabels();
        StartupTitle = _loc.Get("vis.startup");
    }

    [RelayCommand]
    private void ReloadStartup()
    {
        StartupItems.Clear();
        foreach (var e in _app.Startup.GetStartupEntries())
            StartupItems.Add(new StartupRowViewModel(e, _app));
    }

    [RelayCommand]
    private void OpenPagefile()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "SystemPropertiesAdvanced.exe",
            UseShellExecute = true
        });
    }
}

public sealed class DnsOptionItem
{
    public required DnsPresetKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string Id { get; init; }
}

public partial class NetworkViewModel : TweaksCategoryViewModel
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;

    public NetworkViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null)
        : base("Network", "net.title", "net.subtitle", app, loc, confirm, confirmAntiCheat)
    {
        _app = app;
        _loc = loc;
        DnsOptions = new ObservableCollection<DnsOptionItem>();
        RebuildDnsOptions();
        SelectDetectedDns();
    }

    public ObservableCollection<DnsOptionItem> DnsOptions { get; }
    [ObservableProperty] private DnsOptionItem? _selectedDnsOption;
    [ObservableProperty] private string _dnsTitle = "";
    [ObservableProperty] private string _dnsDescription = "";
    [ObservableProperty] private string _dnsApplyLabel = "";
    [ObservableProperty] private string _flushDnsLabel = "";
    [ObservableProperty] private string _tweaksSectionTitle = "";
    [ObservableProperty] private string _dnsStatus = "";
    [ObservableProperty] private bool _isBusy;
    public bool CanChangeDns => !IsBusy;

    public new void RefreshLabels()
    {
        base.RefreshLabels();
        DnsTitle = _loc.Get("net.dns.title");
        DnsDescription = _loc.Get("net.dns.desc");
        DnsApplyLabel = _loc.Get("net.dns.apply");
        FlushDnsLabel = _loc.Get("net.dns.flush");
        TweaksSectionTitle = _loc.Get("net.tweaks");
        var selectedId = SelectedDnsOption?.Id;
        RebuildDnsOptions();
        SelectedDnsOption = DnsOptions.FirstOrDefault(o => o.Id == selectedId) ?? DnsOptions.FirstOrDefault();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanChangeDns));

    private void RebuildDnsOptions()
    {
        DnsOptions.Clear();
        foreach (var p in DnsChangerService.Presets)
        {
            DnsOptions.Add(new DnsOptionItem
            {
                Kind = p.Kind,
                Id = p.Id,
                DisplayName = _loc.Get(p.DisplayNameKey)
            });
        }
    }

    private void SelectDetectedDns()
    {
        var kind = _app.Dns.DetectCurrentPreset();
        SelectedDnsOption = DnsOptions.FirstOrDefault(o => o.Kind == kind) ?? DnsOptions.FirstOrDefault();
        DnsStatus = string.Format(_loc.Get("net.dns.detected"), SelectedDnsOption?.DisplayName ?? "-");
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (SelectedDnsOption is null || IsBusy) return;
        IsBusy = true;
        DnsStatus = _loc.Get("net.dns.applying");
        try
        {
            var kind = SelectedDnsOption.Kind;
            var result = await Task.Run(() => _app.Dns.ApplyPreset(kind)).ConfigureAwait(true);
            DnsStatus = result.Success
                ? string.Format(_loc.Get("net.dns.applied"), SelectedDnsOption.DisplayName, result.AdaptersUpdated)
                : (_loc.Get("common.adminRequired") + " " + result.Message);

            if (!result.Success)
                MessageBox.Show(result.Message, _loc.Get("net.dns.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (!result.FlushDnsSucceeded)
                MessageBox.Show(result.Message, _loc.Get("net.dns.title"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DnsStatus = ex.Message;
            MessageBox.Show(ex.Message, _loc.Get("net.dns.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _app.Dns.FlushDnsCache()).ConfigureAwait(true);
            DnsStatus = result.Success
                ? _loc.Get("net.dns.flushOk")
                : (result.ErrorMessage ?? _loc.Get("net.dns.flushFail"));
            if (!result.Success)
                MessageBox.Show(DnsStatus, _loc.Get("net.dns.flush"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class StartupRowViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly StartupEntry _entry;
    private int _gate;

    public StartupRowViewModel(StartupEntry entry, AppServices app)
    {
        _entry = entry;
        _app = app;
        Name = entry.Name;
        CommandText = entry.Command;
        Hive = entry.Hive;
        _isEnabled = entry.IsEnabled;
    }

    public string Name { get; }
    public string CommandText { get; }
    public string Hive { get; }
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (Interlocked.Exchange(ref _gate, 1) == 1) return;

        var desired = !IsEnabled;
        var previous = IsEnabled;
        IsBusy = true;
        IsEnabled = desired;

        try
        {
            await Task.Run(() => _app.Startup.SetEnabled(_entry, desired)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsEnabled = previous;
            MessageBox.Show(ex.Message, "WinCleaner");
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _gate, 0);
        }
    }
}

public partial class ToolsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<Task<bool>> _confirmAntiCheat;
    private List<TweakItemViewModel> _all = new();

    public ToolsViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null)
    {
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _confirmAntiCheat = confirmAntiCheat ?? confirm;
        _takeOwnershipEnabled = app.SystemHealth.IsTakeOwnershipMenuEnabled();
        RefreshLabels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _maintenanceTitle = "";
    [ObservableProperty] private string _tweaksSectionTitle = "";
    [ObservableProperty] private string _cleanTempLabel = "";
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
    [ObservableProperty] private bool _takeOwnershipEnabled;
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<TweakItemViewModel> Items { get; } = new();
    public bool CanRunTools => !IsBusy;

    public void RefreshLabels()
    {
        Title = _loc.Get("tools.title");
        Subtitle = _loc.Get("tools.subtitle");
        MaintenanceTitle = _loc.Get("tools.maintenance");
        TweaksSectionTitle = _loc.Get("tools.registrySafe");
        CleanTempLabel = _loc.Get("tools.cleanTemp");
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
        foreach (var item in _all)
            item.RefreshLabels();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunTools));

    private void Reload()
    {
        _all = CatalogLoader.LoadTweaks()
            .Where(t => t.Category.Equals("Tools", StringComparison.OrdinalIgnoreCase))
            .Where(t => OsCompatibility.IsCompatible(t.MinBuild, t.Win11Only, _app.Os))
            .Select(entry =>
            {
                var applied = false;
                var action = _app.ActionFactory.CreateFromTweak(entry);
                if (action is RegistryChangeAction reg) applied = reg.IsApplied();
                else if (action is MultiRegistryChangeAction multi) applied = multi.IsApplied();
                else if (action is CommandChangeAction cmd) applied = cmd.IsApplied();
                return new TweakItemViewModel(
                    entry.Id, entry.DisplayNameKey, entry.DescriptionKey,
                    entry.Category, RiskParser.Parse(entry.Risk), _app, _loc,
                    () => _app.ActionFactory.CreateFromTweak(entry)!, applied, true, _confirm,
                    entry.RequiresAntiCheatConfirm, _confirmAntiCheat);
            }).ToList();
        Filter(null);
    }

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var i in _all.Where(x => x.MatchesSearch(query)))
            Items.Add(i);
    }

    public IReadOnlyList<string> GetAppliedTweakIds() =>
        _all.Where(i => i.IsApplied).Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    [RelayCommand]
    private void CleanTemp()
    {
        var freed = _app.TempCleanup.CleanTempFolders();
        MessageBox.Show(
            string.Format(_loc.Get("tools.tempFreed"), _loc.FormatBytes(freed)),
            "WinCleaner");
    }

    [RelayCommand]
    private void OpenDiskCleanup()
    {
        Process.Start(new ProcessStartInfo { FileName = "cleanmgr.exe", UseShellExecute = true });
    }

    [RelayCommand]
    private async Task RunSfcDismRepairAsync()
    {
        if (IsBusy) return;
        var confirm = MessageBox.Show(
            _loc.Get("tools.sfcDism.confirm"),
            _loc.Get("tools.sfcDism"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);
        if (confirm != MessageBoxResult.OK) return;

        await RunWithProgressWindowAsync(
            _loc.Get("tools.sfcDism"),
            (progress, ct) => _app.SystemHealth.RunSfcAndDismRepairAsync(progress, ct)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RunWinsxsCleanupAsync()
    {
        if (IsBusy) return;
        var warn = MessageBox.Show(
            _loc.Get("tools.winsxs.confirm"),
            _loc.Get("tools.winsxs"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (warn != MessageBoxResult.OK) return;
        if (!await _confirm().ConfigureAwait(true)) return;

        await RunWithProgressWindowAsync(
            _loc.Get("tools.winsxs"),
            (progress, ct) => _app.SystemHealth.RunComponentStoreCleanupAsync(progress, ct)).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CreateGodMode()
    {
        try
        {
            var path = _app.SystemHealth.CreateGodModeFolder();
            MessageBox.Show(
                string.Format(_loc.Get("tools.godMode.done"), path),
                "WinCleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task ResetWindowsUpdateAsync()
    {
        if (IsBusy) return;
        var confirm = MessageBox.Show(
            _loc.Get("tools.wuReset.confirm"),
            _loc.Get("tools.wuReset"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        await RunWithProgressWindowAsync(
            _loc.Get("tools.wuReset"),
            (progress, ct) => _app.SystemHealth.ResetWindowsUpdateAsync(progress, ct)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetNetworkStackAsync()
    {
        if (IsBusy) return;
        var confirm = MessageBox.Show(
            _loc.Get("tools.networkReset.confirm"),
            _loc.Get("tools.networkReset"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        await RunWithProgressWindowAsync(
            _loc.Get("tools.networkReset"),
            (progress, ct) => _app.SystemHealth.ResetNetworkStackAsync(progress, ct)).ConfigureAwait(true);

        MessageBox.Show(
            _loc.Get("tools.networkReset.reboot"),
            _loc.Get("tools.networkReset"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    private void OpenMemoryDiagnostic()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "mdsched.exe"),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task AnalyzeDiskSpaceAsync()
    {
        if (IsBusy) return;
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        await RunWithProgressWindowAsync(
            _loc.Get("tools.diskAnalyze"),
            (progress, ct) => _app.SystemHealth.AnalyzeDiskSpaceAsync(root, progress, ct)).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleTakeOwnership()
    {
        var desired = !TakeOwnershipEnabled;
        try
        {
            if (!PrivilegeHelper.IsAdministrator())
            {
                MessageBox.Show(_loc.Get("common.adminRequired"), "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _app.SystemHealth.SetTakeOwnershipMenu(desired, _loc.Get("tools.takeOwn.menu"));
            TakeOwnershipEnabled = _app.SystemHealth.IsTakeOwnershipMenuEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                _loc.Get("common.adminRequired") + Environment.NewLine + ex.Message,
                "WinCleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TakeOwnershipEnabled = _app.SystemHealth.IsTakeOwnershipMenuEnabled();
        }
    }

    private async Task RunWithProgressWindowAsync(
        string title,
        Func<IProgress<string>, CancellationToken, Task> work)
    {
        IsBusy = true;
        var window = new CommandProgressWindow(title)
        {
            Owner = Application.Current?.MainWindow
        };
        window.Show();

        var progress = new Progress<string>(window.AppendLine);
        try
        {
            await work(progress, CancellationToken.None).ConfigureAwait(true);
            window.MarkCompleted(true, _loc.Get("tools.progress.done"));
        }
        catch (Exception ex)
        {
            window.AppendLine("");
            window.AppendLine("ERROR: " + ex.Message);
            window.MarkCompleted(false, _loc.Get("tools.progress.failed"));
            MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class PresetsViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly MainViewModel _main;

    public PresetsViewModel(AppServices app, LocalizationService loc, MainViewModel main)
    {
        _app = app;
        _loc = loc;
        _main = main;
        RefreshLabels();
        Reload();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _customName = "MyProfile";
    public ObservableCollection<PresetRowViewModel> Items { get; } = new();

    public void RefreshLabels()
    {
        Title = _loc.Get("pre.title");
        Subtitle = _loc.Get("pre.subtitle");
        foreach (var i in Items) i.Refresh(_loc);
    }

    private void Reload()
    {
        Items.Clear();
        foreach (var p in CatalogLoader.LoadPresets())
            Items.Add(new PresetRowViewModel(p, _loc));
    }

    [RelayCommand]
    private async Task ApplyPresetAsync(PresetRowViewModel? row)
    {
        if (row is null) return;
        if (row.Id == "preset-defaults")
        {
            await _main.UndoAllCommand.ExecuteAsync(null);
            return;
        }

        var preset = CatalogLoader.LoadPresets().First(p => p.Id == row.Id);
        var actions = _app.Presets.BuildPreview(preset);
        await _main.ShowPreviewAndApplyAsync(actions, row.Title);
    }

    [RelayCommand]
    private void ExportCustom()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON|*.json",
            FileName = $"{CustomName}.json"
        };
        if (dialog.ShowDialog() != true) return;

        var tweakIds = _main.Gaming.GetAppliedTweakIds()
            .Concat(_main.Telemetry.GetAppliedTweakIds())
            .Concat(_main.Visual.GetAppliedTweakIds())
            .Concat(_main.Win11Ui.GetAppliedTweakIds())
            .Concat(_main.Network.GetAppliedTweakIds())
            .Concat(_main.Tools.GetAppliedTweakIds())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profile = new CustomProfileDocument
        {
            SchemaVersion = CustomProfileDocument.CurrentSchemaVersion,
            Id = "custom-" + Guid.NewGuid().ToString("N")[..8],
            Name = string.IsNullOrWhiteSpace(CustomName) ? "MyProfile" : CustomName.Trim(),
            Description = "WinCleaner full profile export",
            TweakIds = tweakIds,
            Services = _main.ServicesPage.GetExportServiceStates().ToList()
        };

        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dialog.FileName, json);
        MessageBox.Show(_loc.Get("pre.exported"), "WinCleaner");
    }

    [RelayCommand]
    private async Task ImportCustomAsync()
    {
        var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            var profile = JsonSerializer.Deserialize<CustomProfileDocument>(json);
            if (profile is null) return;

            // Normalize legacy fields into the v2 shape when needed
            if (string.IsNullOrWhiteSpace(profile.Name) && !string.IsNullOrWhiteSpace(profile.DisplayNameKey))
                profile.Name = profile.DisplayNameKey;
            if (string.IsNullOrWhiteSpace(profile.Description) && !string.IsNullOrWhiteSpace(profile.DescriptionKey))
                profile.Description = profile.DescriptionKey;

            var actions = _app.Presets.BuildPreview(profile);
            if (actions.Count == 0)
            {
                MessageBox.Show(_loc.Get("pre.importedEmpty"), "WinCleaner");
                return;
            }

            await _main.ShowPreviewAndApplyAsync(actions, profile.ResolveDisplayName());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner");
        }
    }
}

public partial class PresetRowViewModel : ObservableObject
{
    private readonly PresetCatalogEntry _entry;

    public PresetRowViewModel(PresetCatalogEntry entry, LocalizationService loc)
    {
        _entry = entry;
        Id = entry.Id;
        Refresh(loc);
    }

    public string Id { get; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";

    public void Refresh(LocalizationService loc)
    {
        Title = loc.Get(_entry.DisplayNameKey);
        Description = loc.Get(_entry.DescriptionKey);
        if (Title == _entry.DisplayNameKey && !_entry.DisplayNameKey.Contains('.'))
            Title = _entry.DisplayNameKey;
    }
}

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
        RefreshLabels();
    }

    public ObservableCollection<LanguageOption> Languages { get; }
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private LanguageOption? _selectedLanguage;
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

    private void PresentUpdateResult(UpdateCheckResult result, bool notifyWhenCurrent)
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
            if (answer == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = result.ReleaseUrl, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
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
public partial class LanguageOption : ObservableObject
{
    public LanguageOption(string code, string displayName)
    {
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    [ObservableProperty] private string _displayName;

    public override string ToString() => DisplayName;
}

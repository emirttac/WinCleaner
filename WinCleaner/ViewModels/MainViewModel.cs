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

namespace WinCleaner.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LocalizationService _loc;

    public AppServices App { get; }
    public LocalizationService Loc => _loc;

    public DashboardViewModel Dashboard { get; }
    public ServicesViewModel ServicesPage { get; }
    public TweaksCategoryViewModel Telemetry { get; }
    public BloatwareViewModel Bloatware { get; }
    public TweaksCategoryViewModel Gaming { get; }
    public VisualViewModel Visual { get; }
    public TweaksCategoryViewModel Win11Ui { get; }
    public NetworkViewModel Network { get; }
    public ToolsViewModel Tools { get; }
    public PackageInstallerViewModel PackageInstaller { get; }
    public PresetsViewModel Presets { get; }
    public SettingsViewModel SettingsPage { get; }

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedNav = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _osBanner = "";

    // Localized nav labels (refresh on language change)
    [ObservableProperty] private string _navDashboard = "";
    [ObservableProperty] private string _navServices = "";
    [ObservableProperty] private string _navTelemetry = "";
    [ObservableProperty] private string _navBloatware = "";
    [ObservableProperty] private string _navGaming = "";
    [ObservableProperty] private string _navVisual = "";
    [ObservableProperty] private string _navWin11 = "";
    [ObservableProperty] private string _navNetwork = "";
    [ObservableProperty] private string _navTools = "";
    [ObservableProperty] private string _navInstaller = "";
    [ObservableProperty] private string _navPresets = "";
    [ObservableProperty] private string _navSettings = "";
    [ObservableProperty] private string _searchPlaceholder = "";
    [ObservableProperty] private string _appTitle = "WinCleaner";
    [ObservableProperty] private string _undoLabel = "";

    public MainViewModel()
    {
        _loc = new LocalizationService();
        App = new AppServices(key => _loc.Get(key));
        _loc.Initialize(App.Settings.Language);
        App.SetLocalizer(key => _loc.Get(key));

        Dashboard = new DashboardViewModel(App, _loc, this);
        ServicesPage = new ServicesViewModel(App, _loc, ConfirmDangerousAsync);
        Telemetry = new TweaksCategoryViewModel("Telemetry", "tel.title", "tel.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        Bloatware = new BloatwareViewModel(App, _loc, ConfirmDangerousAsync);
        Gaming = new TweaksCategoryViewModel("Gaming", "gam.title", "gam.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        Visual = new VisualViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        Win11Ui = new TweaksCategoryViewModel("Windows11UI", "win11.title", "win11.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        Network = new NetworkViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        Tools = new ToolsViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync);
        PackageInstaller = new PackageInstallerViewModel(App, _loc);
        Presets = new PresetsViewModel(App, _loc, this);
        SettingsPage = new SettingsViewModel(App, _loc, this);

        OsBanner = App.Os.DisplayName + (App.Os.IsSupported ? "" : " (unsupported)");
        App.RestorePoints.NotifyMissingSessionCheckpoint = ShowMissingRestoreWarningAsync;
        RefreshLabels();
        _loc.LanguageChanged += RefreshLabels;
        SelectedNav = "dashboard";
        AppLog.Info("MainViewModel initialized");
        _ = SettingsPage.CheckForUpdatesOnStartupAsync();
    }

    private Task ShowMissingRestoreWarningAsync()
    {
        return UiThread.RunAsync(() =>
        {
            MessageBox.Show(
                _loc.Get("restore.warnBody"),
                _loc.Get("restore.warnTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            StatusText = _loc.Get("restore.creating");
        });
    }

    private void RefreshLabels()
    {
        AppTitle = _loc.Get("app.title");
        NavDashboard = _loc.Get("nav.dashboard");
        NavServices = _loc.Get("nav.services");
        NavTelemetry = _loc.Get("nav.telemetry");
        NavBloatware = _loc.Get("nav.bloatware");
        NavGaming = _loc.Get("nav.gaming");
        NavVisual = _loc.Get("nav.visual");
        NavWin11 = _loc.Get("nav.win11");
        NavNetwork = _loc.Get("nav.network");
        NavTools = _loc.Get("nav.tools");
        NavInstaller = _loc.Get("nav.installer");
        NavPresets = _loc.Get("nav.presets");
        NavSettings = _loc.Get("nav.settings");
        SearchPlaceholder = _loc.Get("common.search");
        UndoLabel = _loc.Get("common.undo");
        Telemetry.RefreshLabels();
        Gaming.RefreshLabels();
        Network.RefreshLabels();
        Win11Ui.RefreshLabels();
        ServicesPage.RefreshLabels();
        Bloatware.RefreshLabels();
        Visual.RefreshLabels();
        Tools.RefreshLabels();
        PackageInstaller.RefreshLabels();
        Presets.RefreshLabels();
        SettingsPage.RefreshLabels();
        Dashboard.RefreshLabels();
    }

    partial void OnSelectedNavChanged(string value)
    {
        try
        {
            AppLog.Info($"Navigate request: {value}");
            CurrentPage = value switch
            {
                "services" => ServicesPage,
                "telemetry" => Telemetry,
                "bloatware" => Bloatware,
                "gaming" => Gaming,
                "visual" => Visual,
                "win11" => Win11Ui,
                "network" => Network,
                "tools" => Tools,
                "installer" => PackageInstaller,
                "presets" => Presets,
                "settings" => SettingsPage,
                _ => Dashboard
            };
            AppLog.Info($"Navigate OK -> {CurrentPage?.GetType().Name}");

            if (CurrentPage is DashboardViewModel dash)
                dash.RefreshRecent();

            if (!string.IsNullOrWhiteSpace(SearchText))
                ApplySearch(SearchText);
        }
        catch (Exception ex)
        {
            AppLog.Crash("OnSelectedNavChanged", ex);
            StatusText = "Navigation error — see log";
        }
    }

    [RelayCommand]
    private void Navigate(string? page)
    {
        SelectedNav = page ?? "dashboard";
    }

    partial void OnSearchTextChanged(string value) => ApplySearch(value);

    private void ApplySearch(string query)
    {
        ServicesPage.Filter(query);
        Telemetry.Filter(query);
        Gaming.Filter(query);
        Network.Filter(query);
        Win11Ui.Filter(query);
        Visual.Filter(query);
        Tools.Filter(query);
        Bloatware.Filter(query);
        PackageInstaller.Filter(query);
    }

    [RelayCommand]
    private async Task UndoAllAsync()
    {
        var entries = App.ChangeLog.GetRevertableEntries().ToList();
        if (entries.Count == 0)
        {
            MessageBox.Show(_loc.Get("msg.noReversibleChanges"), _loc.Get("app.title"));
            return;
        }

        var confirm = MessageBox.Show(
            _loc.Format("msg.revertConfirm", entries.Count),
            _loc.Get("app.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var entry in entries)
        {
            var action = TryResolveAction(entry.ActionId);
            if (action is null) continue;
            action.SeedPreviousState(entry.OldValue);
            await App.Executor.ExecuteAsync(action, isRevert: true);
        }

        StatusText = _loc.Get("msg.undoCompleted");
        Dashboard.RefreshRecent();
    }

    private IChangeAction? TryResolveAction(string actionId)
    {
        var tweak = CatalogLoader.LoadTweaks().FirstOrDefault(t => t.Id == actionId);
        if (tweak is not null) return App.ActionFactory.CreateFromTweak(tweak);

        var svc = CatalogLoader.LoadServices().FirstOrDefault(s => s.Id == actionId);
        if (svc is not null)
            return App.ActionFactory.CreateServiceAction(svc.Id, svc.ServiceName, svc.DescriptionKey, svc.Category,
                RiskParser.Parse(svc.Risk), svc.ServiceName, svc.RecommendedStart, svc.ServiceAliases);

        return null;
    }

    public Task<bool> ConfirmDangerousAsync()
    {
        var dlg = new Views.ConfirmDangerDialog(
            title: _loc.Get("danger.title"),
            message: _loc.Get("danger.message"),
            acknowledgeLabel: _loc.Get("danger.ack"));
        dlg.Owner = Application.Current.MainWindow;
        return Task.FromResult(dlg.ShowDialog() == true);
    }

    public Task<bool> ConfirmAntiCheatAsync()
    {
        var dlg = new Views.ConfirmDangerDialog(
            title: _loc.Get("danger.anticheat.title"),
            message: _loc.Get("danger.anticheat.message"),
            acknowledgeLabel: _loc.Get("danger.ack"));
        dlg.Owner = Application.Current.MainWindow;
        return Task.FromResult(dlg.ShowDialog() == true);
    }

    public async Task ShowPreviewAndApplyAsync(IReadOnlyList<IChangeAction> actions, string title)
    {
        if (actions.Count == 0)
        {
            MessageBox.Show(_loc.Get("msg.nothingToApply"), _loc.Get("app.title"));
            return;
        }

        var preview = string.Join(Environment.NewLine, actions.Select(a => $"• [{a.Risk}] {a.DisplayName}"));
        var result = MessageBox.Show(
            $"{_loc.Get("dlg.previewTitle")}:{Environment.NewLine}{Environment.NewLine}{preview}{Environment.NewLine}{Environment.NewLine}{_loc.Get("common.apply")}?",
            title, MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (result != MessageBoxResult.OK) return;

        if (actions.Any(a => a.RequiresAntiCheatConfirm))
        {
            if (!await ConfirmAntiCheatAsync()) return;
        }
        else if (actions.Any(a => a.Risk == RiskLevel.Dangerous))
        {
            if (!await ConfirmDangerousAsync()) return;
        }

        var progress = new Progress<string>(s => StatusText = s);
        await App.Executor.ExecuteManyAsync(actions, progress);
        StatusText = _loc.Get("msg.done");
        Dashboard.RefreshRecent();
    }
}

public partial class DashboardViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly MainViewModel _main;

    public DashboardViewModel(AppServices app, LocalizationService loc, MainViewModel main)
    {
        _app = app;
        _loc = loc;
        _main = main;
        RefreshLabels();
        RefreshRecent();
        _app.ChangeLog.Changed += OnChangeLogChanged;
        _app.RestorePoints.SessionCheckpointChanged += OnRestoreChanged;
        CpuName = app.Hardware.CpuName;
        GpuName = app.Hardware.GpuName;
        RamText = loc.FormatBytes(app.Hardware.TotalRamBytes);
        OsText = app.Hardware.OsDisplayName;
        DiskText = string.Join(" | ", app.Hardware.Disks.Select(d =>
            string.Format(CultureInfo.CurrentCulture, loc.Get("dash.diskFreeOf"),
                d.Name, loc.FormatBytes(d.FreeBytes), loc.FormatBytes(d.TotalBytes))));
    }

    private void OnChangeLogChanged() => UiThread.Run(RefreshRecent);
    private void OnRestoreChanged() => UiThread.Run(RefreshRestoreStatus);

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _gpuName = "";
    [ObservableProperty] private string _ramText = "";
    [ObservableProperty] private string _osText = "";
    [ObservableProperty] private string _diskText = "";
    [ObservableProperty] private string _recentTitle = "";
    [ObservableProperty] private string _quickTitle = "";
    [ObservableProperty] private string _recentEmpty = "";
    [ObservableProperty] private string _restoreCardTitle = "";
    [ObservableProperty] private string _restoreStatusText = "";
    [ObservableProperty] private string _createRestoreLabel = "";
    [ObservableProperty] private string _applyGamerLabel = "";
    [ObservableProperty] private string _flushDnsLabel = "";
    [ObservableProperty] private string _cleanTempLabel = "";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<string> RecentChanges { get; } = new();
    public bool HasRecentChanges => RecentChanges.Count > 0;
    public bool CanRunQuickActions => !IsBusy;

    public void RefreshLabels()
    {
        Title = _loc.Get("dash.title");
        Subtitle = _loc.Get("dash.subtitle");
        RecentTitle = _loc.Get("dash.recent");
        QuickTitle = _loc.Get("dash.quick");
        RecentEmpty = _loc.Get("dash.recentEmpty");
        RestoreCardTitle = _loc.Get("restore.cardTitle");
        CreateRestoreLabel = _loc.Get("dash.createRestore");
        ApplyGamerLabel = _loc.Get("dash.applyGamer");
        FlushDnsLabel = _loc.Get("dash.flushDns");
        CleanTempLabel = _loc.Get("dash.cleanTemp");
        RefreshRestoreStatus();
    }

    public void RefreshRestoreStatus()
    {
        RestoreStatusText = _app.RestorePoints.HasSessionCheckpoint
            ? _loc.Get("restore.statusOk")
            : _loc.Get("restore.statusNone");
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunQuickActions));

    public void RefreshRecent()
    {
        RecentChanges.Clear();
        foreach (var e in _app.ChangeLog.SessionEntries.Reverse().Take(15))
        {
            var status = e.Success ? _loc.Get("common.statusOk") : _loc.Get("common.statusFail");
            RecentChanges.Add($"{e.Timestamp:HH:mm:ss} — {e.DisplayName} ({status})");
        }
        OnPropertyChanged(nameof(HasRecentChanges));
    }

    [RelayCommand]
    private async Task CreateRestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _main.StatusText = _loc.Get("restore.creating");
        try
        {
            var ok = await _app.RestorePoints.CreateManualAsync("WinCleaner").ConfigureAwait(true);
            RefreshRestoreStatus();
            MessageBox.Show(
                ok ? _loc.Get("restore.created") : _loc.Get("restore.failed"),
                _loc.Get("restore.cardTitle"),
                MessageBoxButton.OK,
                ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            _main.StatusText = ok ? _loc.Get("restore.created") : _loc.Get("restore.failed");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("restore.cardTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyGamerAsync()
    {
        if (IsBusy) return;
        var preset = CatalogLoader.LoadPresets().First(p => p.Id == "preset-gamer");
        var actions = _app.Presets.BuildPreview(preset);
        await _main.ShowPreviewAndApplyAsync(actions, _loc.Get("preset.gamer.name"));
        RefreshRecent();
        RefreshRestoreStatus();
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        if (IsBusy) return;
        var tweak = CatalogLoader.LoadTweaks().First(t => t.Id == "tweak-dns-flush");
        var action = _app.ActionFactory.CreateFromTweak(tweak);
        if (action is not null) await _app.Executor.ExecuteAsync(action);
        RefreshRecent();
        RefreshRestoreStatus();
    }

    [RelayCommand]
    private async Task CleanTempAsync()
    {
        if (IsBusy) return;
        if (MessageBox.Show(
                _loc.Get("tools.cleanTemp.confirm"),
                _loc.Get("dash.cleanTemp"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        _main.StatusText = _loc.Get("tools.tempWorking");
        try
        {
            await _app.EnsureSessionRestoreAsync().ConfigureAwait(true);
            var freed = await Task.Run(() => _app.TempCleanup.CleanTempFolders()).ConfigureAwait(true);
            RefreshRestoreStatus();
            MessageBox.Show(_loc.Format("dash.freedApprox", _loc.FormatBytes(freed)), _loc.Get("app.title"));
            _main.StatusText = _loc.Format("dash.freedApprox", _loc.FormatBytes(freed));
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
}

public partial class ServicesViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private List<ServiceRowViewModel> _all = new();

    public ServicesViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm)
    {
        _app = app;
        _loc = loc;
        _confirm = confirm;
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
                    return new ServiceRowViewModel(c, info, _app, _loc, _confirm);
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

    public void Filter(string? query)
    {
        Items.Clear();
        foreach (var item in _all)
        {
            if (SelectedCategory is not null &&
                !string.Equals(SelectedCategory, _loc.Get("common.all"), StringComparison.Ordinal) &&
                item.Category != SelectedCategory)
                continue;
            if (!string.IsNullOrWhiteSpace(query) &&
                !item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !item.ServiceName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            Items.Add(item);
        }
    }

    partial void OnSelectedCategoryChanged(string? value) => Filter(null);

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
        var preview = string.Join(Environment.NewLine, actions.Select(a => $"• {a.DisplayName}"));
        if (MessageBox.Show(
                preview + Environment.NewLine + Environment.NewLine + _loc.Get("msg.disableConfirm"),
                _loc.Get("app.title"),
                MessageBoxButton.OKCancel) != MessageBoxResult.OK)
            return;
        if (actions.Any(a => a.Risk == RiskLevel.Dangerous) && !await _confirm()) return;
        await _app.Executor.ExecuteManyAsync(actions);
        Reload();
    }
}

public partial class ServiceRowViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly ServiceCatalogEntry _entry;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private int _applyGate;

    public ServiceRowViewModel(ServiceCatalogEntry entry, WindowsServiceInfo? info, AppServices app, LocalizationService loc, Func<Task<bool>> confirm)
    {
        _entry = entry;
        _app = app;
        _loc = loc;
        _confirm = confirm;
        Id = entry.Id;
        ServiceName = info?.ServiceName ?? entry.ServiceName;
        Category = entry.Category;
        Risk = RiskParser.Parse(entry.Risk);
        IsInstalled = info is not null;
        IsEnabled = Risk != RiskLevel.Blocked && IsInstalled;
        ApplyLiveInfo(info);
        RefreshLabels();
    }

    public string Id { get; }
    public string ServiceName { get; }
    public string Category { get; }
    public RiskLevel Risk { get; }
    public bool IsInstalled { get; }
    public bool IsEnabled { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _riskLabel = "";
    [ObservableProperty] private string _statusLine = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _startTypeText = "";
    [ObservableProperty] private bool _isDisabled;
    [ObservableProperty] private bool _isBusy;

    public void RefreshLabels()
    {
        Title = _loc.Get(_entry.DisplayNameKey);
        Description = _loc.Get(_entry.DescriptionKey);
        RiskLabel = Risk switch
        {
            RiskLevel.Blocked => _loc.Get("common.risk.blocked"),
            RiskLevel.Dangerous => _loc.Get("common.risk.dangerous"),
            RiskLevel.Caution => _loc.Get("common.risk.caution"),
            _ => _loc.Get("common.risk.safe")
        };
        StatusLine = _loc.Format("svc.statusLine", StatusText, StartTypeText);
    }

    public IChangeAction CreateDisableAction() =>
        _app.ActionFactory.CreateServiceAction(_entry.Id, Title, Description, Category, Risk, ServiceName, "Disabled");

    private void ApplyLiveInfo(WindowsServiceInfo? info)
    {
        StatusText = info is null ? _loc.Get("common.na") : info.Status.ToString();
        StartTypeText = info?.StartType.ToString() ?? _loc.Get("common.na");
        IsDisabled = info?.StartType == System.ServiceProcess.ServiceStartMode.Disabled;
        StatusLine = _loc.Format("svc.statusLine", StatusText, StartTypeText);
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (!IsEnabled) return;
        if (Interlocked.Exchange(ref _applyGate, 1) == 1) return;

        var desiredDisabled = !IsDisabled;
        var previous = IsDisabled;
        IsBusy = true;
        IsDisabled = desiredDisabled;

        try
        {
            if (desiredDisabled && Risk == RiskLevel.Dangerous && !await _confirm().ConfigureAwait(true))
            {
                IsDisabled = previous;
                return;
            }

            ChangeResult result;
            if (desiredDisabled)
            {
                var action = CreateDisableAction();
                result = await _app.Executor.ExecuteAsync(action).ConfigureAwait(true);
            }
            else
            {
                var journal = _app.ChangeLog.GetLatestRevertable(Id);
                if (journal is not null)
                {
                    var action = CreateDisableAction();
                    action.SeedPreviousState(journal.OldValue);
                    result = await _app.Executor.ExecuteAsync(action, isRevert: true).ConfigureAwait(true);
                }
                else
                {
                    var action = _app.ActionFactory.CreateServiceAction(
                        _entry.Id, Title, Description, Category, Risk, ServiceName, "Manual");
                    result = await _app.Executor.ExecuteAsync(action).ConfigureAwait(true);
                }
            }

            if (!result.Success)
            {
                IsDisabled = previous;
                MessageBox.Show(result.Message ?? _loc.Get("msg.failed"), _loc.Get("app.title"));
            }
            else
            {
                ApplyLiveInfo(_app.Services.GetService(ServiceName));
            }
        }
        catch (Exception ex)
        {
            IsDisabled = previous;
            MessageBox.Show(ex.Message, "WinCleaner");
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _applyGate, 0);
        }
    }
}

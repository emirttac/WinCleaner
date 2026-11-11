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
    public GlobalSearchViewModel GlobalSearch { get; }

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
        App.Executor.Confirmation = new UiGateConfirmation(_loc);
        App.SetLocalizer(key => _loc.Get(key));

        Dashboard = new DashboardViewModel(App, _loc, this);
        ServicesPage = new ServicesViewModel(App, _loc, ConfirmDangerousAsync, ShowPreviewAndApplyAsync);
        Telemetry = new TweaksCategoryViewModel("Telemetry", "tel.title", "tel.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        Bloatware = new BloatwareViewModel(App, _loc, ConfirmDangerousAsync, ShowPreviewAndApplyAsync);
        Gaming = new TweaksCategoryViewModel("Gaming", "gam.title", "gam.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        Visual = new VisualViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        Win11Ui = new TweaksCategoryViewModel("Windows11UI", "win11.title", "win11.subtitle", App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        Network = new NetworkViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        Tools = new ToolsViewModel(App, _loc, ConfirmDangerousAsync, ConfirmAntiCheatAsync, ShowPreviewAndApplyAsync);
        PackageInstaller = new PackageInstallerViewModel(App, _loc);
        Presets = new PresetsViewModel(App, _loc, this);
        SettingsPage = new SettingsViewModel(App, _loc, this);
        GlobalSearch = new GlobalSearchViewModel(_loc, this);

        OsBanner = App.Os.DisplayName + (App.Os.IsSupported ? "" : " (unsupported)");
        RefreshLabels();
        _loc.LanguageChanged += RefreshLabels;
        SelectedNav = "dashboard";
        AppLog.Info("MainViewModel initialized");
        _ = SettingsPage.CheckForUpdatesOnStartupAsync();
    }

    /// <summary>
    /// Creates the single session restore point at app open (modal, blocks UI).
    /// Operations during the session do not create additional restore points.
    /// </summary>
    public async Task EnsureStartupRestorePointAsync(Window owner)
    {
        var partial = App.ChangeLog.GetPartialEntries();
        if (partial.Count > 0)
        {
            MessageBox.Show(
                _loc.Get("gate.partial"),
                _loc.Get("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (!App.Settings.AutoRestorePoint || App.RestorePoints.HasSessionCheckpoint)
        {
            Dashboard.RefreshRestoreStatus();
            return;
        }

        await ShowCreatingRestoreDialogAsync(owner, markAutoAttempted: true).ConfigureAwait(true);
        Dashboard.RefreshRestoreStatus();
    }

    public async Task<bool> ShowCreatingRestoreDialogAsync(Window? owner, bool markAutoAttempted = false)
    {
        var ok = false;
        await UiThread.RunAsync(() =>
        {
            var dialog = new CreatingRestorePointDialog(
                async ct =>
                {
                    if (markAutoAttempted)
                    {
                        await App.RestorePoints
                            .EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: false, ct)
                            .ConfigureAwait(false);
                        return App.RestorePoints.HasSessionCheckpoint;
                    }

                    return await App.RestorePoints.CreateManualAsync("WinCleaner", ct).ConfigureAwait(false);
                },
                _loc.Get("restore.creating"),
                _loc.Get("restore.warnBody"));

            if (owner is not null)
                dialog.Owner = owner;

            var main = owner as MainWindow;
            main?.SetBusyOverlay(true);

            try
            {
                dialog.Topmost = true;
                dialog.ShowDialog();
                ok = dialog.Succeeded;
            }
            finally
            {
                main?.SetBusyOverlay(false);
                owner?.Activate();
            }
        }).ConfigureAwait(true);

        StatusText = RestorePointManager.LastOutcome switch
        {
            RestorePointOutcome.Created => _loc.Get("restore.created"),
            RestorePointOutcome.AlreadyExists => _loc.Get("restore.existing"),
            RestorePointOutcome.ProtectionDisabled => _loc.Get("restore.protectionOff"),
            _ => _loc.Get("restore.failed")
        };
        return ok;
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
        GlobalSearch.RefreshLabels();
    }

    private object ResolvePage(string nav) => nav switch
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

    private bool _keepSearchOnNav;

    partial void OnSelectedNavChanged(string value)
    {
        try
        {
            AppLog.Info($"Navigate request: {value}");

            // Clicking the sidebar exits global search unless opened from a search section
            if (!_keepSearchOnNav && !string.IsNullOrWhiteSpace(SearchText))
            {
                _searchText = "";
                OnPropertyChanged(nameof(SearchText));
                ApplyPageFilters(null);
            }

            CurrentPage = ResolvePage(value);
            AppLog.Info($"Navigate OK -> {CurrentPage?.GetType().Name}");

            if (CurrentPage is DashboardViewModel dash)
                dash.RefreshRecent();

            if (_keepSearchOnNav && !string.IsNullOrWhiteSpace(SearchText))
                ApplyPageFilters(SearchText);
        }
        catch (Exception ex)
        {
            AppLog.Crash("OnSelectedNavChanged", ex);
            StatusText = "Navigation error — see log";
        }
    }
}

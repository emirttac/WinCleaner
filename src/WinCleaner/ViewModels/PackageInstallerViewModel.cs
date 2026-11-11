using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCleaner.Core.Operations;
using WinCleaner.Core.Services;
using WinCleaner.Data;
using WinCleaner.Services;

namespace WinCleaner.ViewModels;

public partial class PackageInstallerViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;

    public PackageInstallerViewModel(AppServices app, LocalizationService loc)
    {
        _app = app;
        _loc = loc;
        Categories = new ObservableCollection<InstallerCategoryGroup>();
        RefreshLabels();
        Reload();
        _ = RefreshWingetStatusAsync();
    }

    public ObservableCollection<InstallerCategoryGroup> Categories { get; }

    private string? _globalQuery;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _searchPlaceholder = "";
    [ObservableProperty] private string _localQuery = "";
    [ObservableProperty] private string _installSelectedLabel = "";
    [ObservableProperty] private string _uninstallSelectedLabel = "";
    [ObservableProperty] private string _wingetStatusText = "";
    [ObservableProperty] private string _remediateWingetLabel = "";
    [ObservableProperty] private bool _wingetAvailable;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _isRemediating;
    [ObservableProperty] private string _overallProgressText = "";
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private bool _isOverallIndeterminate;

    public bool CanInstall => WingetAvailable && !IsInstalling && GetSelectedApps().Count > 0;
    public bool CanUninstall => WingetAvailable && !IsInstalling && GetSelectedApps().Count > 0;
    public bool CanRefreshWinget => !IsInstalling && !IsRemediating;
    public bool CanRemediateWinget => !WingetAvailable && !IsInstalling && !IsRemediating;

    public void RefreshLabels()
    {
        Title = _loc.Get("inst.title");
        Subtitle = _loc.Get("inst.subtitle");
        SearchPlaceholder = _loc.Get("inst.search.placeholder");
        InstallSelectedLabel = _loc.Get("inst.installSelected");
        UninstallSelectedLabel = _loc.Get("inst.uninstallSelected");
        RemediateWingetLabel = _loc.Get("inst.wingetRemediate");
        foreach (var cat in Categories)
        {
            cat.Title = _loc.Get(cat.CategoryKey);
            foreach (var app in cat.Apps)
                app.RefreshLabels(_loc);
        }
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanRemediateWinget));
    }

    public void Filter(string? query)
    {
        _globalQuery = query;
        ApplyFilters();
    }

    partial void OnLocalQueryChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        foreach (var cat in Categories)
            cat.ApplyFilter(_globalQuery, LocalQuery);
    }

    public IEnumerable<InstallerAppItemViewModel> SearchAll(string? query) =>
        Categories.SelectMany(c => c.Apps).Where(a => a.MatchesSearch(query));

    private void Reload()
    {
        Categories.Clear();
        var entries = CatalogLoader.LoadInstallerApps();
        foreach (var group in entries.GroupBy(e => e.Category).OrderBy(g => CategorySort(g.Key)))
        {
            var first = group.First();
            var catKey = string.IsNullOrWhiteSpace(first.CategoryKey)
                ? $"inst.cat.{first.Category.ToLowerInvariant()}"
                : first.CategoryKey;
            var cat = new InstallerCategoryGroup(first.Category, catKey, _loc.Get(catKey));
            foreach (var entry in group.OrderBy(e => e.WingetId))
                cat.Apps.Add(new InstallerAppItemViewModel(entry, _loc, OnSelectionChanged));
            Categories.Add(cat);
        }
        ApplyFilters();
    }

    private static int CategorySort(string category) => category switch
    {
        "Browsers" => 0,
        "Communication" => 1,
        "Developer" => 2,
        "Gaming" => 3,
        "Media" => 4,
        "Tools" => 5,
        _ => 9
    };

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
    }

    private List<InstallerAppItemViewModel> GetSelectedApps() =>
        Categories.SelectMany(c => c.Apps).Where(a => a.IsSelected && !a.IsBusy).ToList();

    [RelayCommand]
    private async Task RefreshWingetStatusAsync()
    {
        var avail = await _app.Winget.CheckAvailabilityAsync().ConfigureAwait(true);
        WingetAvailable = avail.Available;
        WingetStatusText = avail.Available
            ? string.Format(_loc.Get("inst.wingetOk"), avail.Version)
            : (avail.ErrorMessage ?? _loc.Get("inst.wingetMissing"));
        if (avail.Available)
        {
            var apps = Categories.SelectMany(c => c.Apps).ToList();
            var installed = await _app.Winget.FindInstalledAsync(apps.Select(a => a.WingetId)).ConfigureAwait(true);
            foreach (var app in apps)
            {
                if (installed.Contains(app.WingetId))
                    app.SetStatus(WingetInstallStatus.AlreadyInstalled, _loc);
            }
        }
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRemediateWinget));
    }

    [RelayCommand]
    private async Task RemediateWingetAsync()
    {
        if (IsRemediating || IsInstalling) return;

        var choice = MessageBox.Show(
            _loc.Get("inst.wingetRemediatePrompt"),
            _loc.Get("inst.title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
            return;

        if (choice == MessageBoxResult.Yes)
        {
            if (!_app.Winget.OpenAppInstallerStore())
            {
                MessageBox.Show(_loc.Get("inst.wingetMissing"), _loc.Get("inst.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return;
        }

        // No = bootstrap
        IsRemediating = true;
        OnPropertyChanged(nameof(CanRemediateWinget));
        OnPropertyChanged(nameof(CanRefreshWinget));
        OverallProgressText = _loc.Get("inst.wingetRemediate");
        IsOverallIndeterminate = true;

        try
        {
            var result = await _app.Winget.TryBootstrapWingetAsync().ConfigureAwait(true);
            if (result.Success)
            {
                MessageBox.Show(_loc.Get("inst.wingetBootstrapOk"), _loc.Get("inst.title"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshWingetStatusAsync().ConfigureAwait(true);
            }
            else
            {
                MessageBox.Show(
                    string.Format(_loc.Get("inst.wingetBootstrapFail"), result.Message),
                    _loc.Get("inst.title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                // Still offer Store as fallback
                _app.Winget.OpenAppInstallerStore();
            }
        }
        finally
        {
            IsRemediating = false;
            IsOverallIndeterminate = false;
            OverallProgressText = "";
            OnPropertyChanged(nameof(CanRemediateWinget));
            OnPropertyChanged(nameof(CanRefreshWinget));
        }
    }

    [RelayCommand]
    private async Task UpgradeSelectedAsync()
    {
        if (IsInstalling) return;
        var selected = GetSelectedApps();
        if (selected.Count == 0) return;
        IsInstalling = true;
        try
        {
            foreach (var app in selected)
            {
                app.SetStatus(WingetInstallStatus.Installing, _loc);
                var result = await _app.Winget.UpgradeAsync(app.WingetId).ConfigureAwait(true);
                app.SetStatus(result.Status, _loc, result.ErrorMessage);
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (IsInstalling) return;

        var selected = GetSelectedApps();
        if (selected.Count == 0) return;

        var avail = await _app.Winget.CheckAvailabilityAsync().ConfigureAwait(true);
        WingetAvailable = avail.Available;
        if (!avail.Available)
        {
            WingetStatusText = avail.ErrorMessage ?? _loc.Get("inst.wingetMissing");
            OnPropertyChanged(nameof(CanRemediateWinget));
            var choice = MessageBox.Show(
                _loc.Get("inst.wingetRemediatePrompt"),
                _loc.Get("inst.title"),
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
                _app.Winget.OpenAppInstallerStore();
            else if (choice == MessageBoxResult.No)
            {
                IsRemediating = true;
                OnPropertyChanged(nameof(CanRemediateWinget));
                try
                {
                    var result = await _app.Winget.TryBootstrapWingetAsync().ConfigureAwait(true);
                    if (result.Success)
                    {
                        MessageBox.Show(_loc.Get("inst.wingetBootstrapOk"), _loc.Get("inst.title"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await RefreshWingetStatusAsync().ConfigureAwait(true);
                    }
                    else
                    {
                        MessageBox.Show(
                            string.Format(_loc.Get("inst.wingetBootstrapFail"), result.Message),
                            _loc.Get("inst.title"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        _app.Winget.OpenAppInstallerStore();
                    }
                }
                finally
                {
                    IsRemediating = false;
                    OnPropertyChanged(nameof(CanRemediateWinget));
                }
            }
            OnPropertyChanged(nameof(CanInstall));
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(_loc.Get("inst.confirm"), selected.Count),
            _loc.Get("inst.title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        IsInstalling = true;
        IsOverallIndeterminate = true;
        OverallProgress = 0;
        OverallProgressText = _loc.Get("inst.progress.running");
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRefreshWinget));

        try
        {
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("inst.title"), token =>
                _app.RunWorkAsync(_loc.Get("inst.title"), true, async ctx =>
                {
                    var ct = ctx.Token;
                    var done = 0;
                    foreach (var app in selected)
                        app.SetStatus(WingetInstallStatus.Queued, _loc);

                    foreach (var app in selected)
                    {
                        ct.ThrowIfCancellationRequested();
                        app.IsBusy = true;
                        app.IsProgressIndeterminate = true;
                        app.SetStatus(WingetInstallStatus.Starting, _loc);
                        OverallProgressText = string.Format(_loc.Get("inst.progress.current"), app.Title, done + 1, selected.Count);
                        ctx.Progress.Report(new OperationStep(OverallProgressText, null));

                        var progress = new Progress<string>(phase =>
                        {
                            var status = phase switch
                            {
                                "installing" => WingetInstallStatus.Installing,
                                "completed" => WingetInstallStatus.Completed,
                                "already" => WingetInstallStatus.AlreadyInstalled,
                                "failed" => WingetInstallStatus.Failed,
                                "cancelled" => WingetInstallStatus.Cancelled,
                                _ => WingetInstallStatus.Starting
                            };
                            app.SetStatus(status, _loc);
                            app.IsProgressIndeterminate = status is WingetInstallStatus.Starting or WingetInstallStatus.Installing;
                        });

                        var result = await _app.Winget.InstallAsync(app.WingetId, progress, ct).ConfigureAwait(true);
                        app.SetStatus(result.Status, _loc, result.ErrorMessage);
                        app.IsProgressIndeterminate = false;
                        app.IsBusy = false;
                        if (result.Success)
                            app.IsSelected = false;

                        done++;
                        IsOverallIndeterminate = false;
                        OverallProgress = selected.Count == 0 ? 0 : (double)done / selected.Count * 100;
                        ctx.Progress.Report(new OperationStep(OverallProgressText, OverallProgress));
                    }
                }, token)).ConfigureAwait(true);

            OverallProgressText = op.Cancelled
                ? _loc.Get("inst.progress.cancelled")
                : op.Success ? _loc.Get("inst.progress.done") : (op.Message ?? _loc.Get("inst.progress.done"));
            if (op.Cancelled)
            {
                foreach (var app in selected.Where(a => a.Status is WingetInstallStatus.Queued or WingetInstallStatus.Starting or WingetInstallStatus.Installing))
                {
                    app.SetStatus(WingetInstallStatus.Cancelled, _loc);
                    app.IsBusy = false;
                    app.IsProgressIndeterminate = false;
                }
            }
            else if (!op.Success && !string.IsNullOrWhiteSpace(op.Message))
                MessageBox.Show(op.Message, _loc.Get("inst.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsInstalling = false;
            IsOverallIndeterminate = false;
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(CanRefreshWinget));
        }
    }

    [RelayCommand]
    private async Task UninstallSelectedAsync()
    {
        if (IsInstalling) return;

        var selected = GetSelectedApps();
        if (selected.Count == 0) return;

        var avail = await _app.Winget.CheckAvailabilityAsync().ConfigureAwait(true);
        WingetAvailable = avail.Available;
        if (!avail.Available)
        {
            WingetStatusText = avail.ErrorMessage ?? _loc.Get("inst.wingetMissing");
            OnPropertyChanged(nameof(CanRemediateWinget));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(_loc.Get("inst.uninstallConfirm"), selected.Count),
            _loc.Get("inst.title"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        IsInstalling = true;
        IsOverallIndeterminate = true;
        OverallProgress = 0;
        OverallProgressText = _loc.Get("inst.progress.uninstallRunning");
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
        OnPropertyChanged(nameof(CanRefreshWinget));

        try
        {
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("inst.uninstallSelected"), token =>
                _app.RunWorkAsync(_loc.Get("inst.uninstallSelected"), true, async ctx =>
                {
                    var ct = ctx.Token;
                    var done = 0;
                    foreach (var app in selected)
                        app.SetStatus(WingetInstallStatus.Queued, _loc);

                    foreach (var app in selected)
                    {
                        ct.ThrowIfCancellationRequested();
                        app.IsBusy = true;
                        app.IsProgressIndeterminate = true;
                        app.SetStatus(WingetInstallStatus.Starting, _loc);
                        OverallProgressText = string.Format(_loc.Get("inst.progress.current"), app.Title, done + 1, selected.Count);
                        ctx.Progress.Report(new OperationStep(OverallProgressText, null));

                        var progress = new Progress<string>(phase =>
                        {
                            var status = phase switch
                            {
                                "installing" => WingetInstallStatus.Installing,
                                "completed" => WingetInstallStatus.Completed,
                                "already" => WingetInstallStatus.AlreadyInstalled,
                                "failed" => WingetInstallStatus.Failed,
                                "cancelled" => WingetInstallStatus.Cancelled,
                                _ => WingetInstallStatus.Starting
                            };
                            app.SetStatus(status, _loc);
                            app.IsProgressIndeterminate = status is WingetInstallStatus.Starting or WingetInstallStatus.Installing;
                        });

                        var result = await _app.Winget.UninstallAsync(app.WingetId, progress, ct).ConfigureAwait(true);
                        app.SetStatus(result.Status, _loc, result.ErrorMessage);
                        app.IsProgressIndeterminate = false;
                        app.IsBusy = false;
                        if (result.Success)
                            app.IsSelected = false;

                        done++;
                        IsOverallIndeterminate = false;
                        OverallProgress = selected.Count == 0 ? 0 : (double)done / selected.Count * 100;
                        ctx.Progress.Report(new OperationStep(OverallProgressText, OverallProgress));
                    }
                }, token)).ConfigureAwait(true);

            OverallProgressText = op.Cancelled
                ? _loc.Get("inst.progress.cancelled")
                : op.Success ? _loc.Get("inst.progress.uninstallDone") : (op.Message ?? _loc.Get("inst.progress.uninstallDone"));
            if (op.Cancelled)
            {
                foreach (var app in selected.Where(a => a.Status is WingetInstallStatus.Queued or WingetInstallStatus.Starting or WingetInstallStatus.Installing))
                {
                    app.SetStatus(WingetInstallStatus.Cancelled, _loc);
                    app.IsBusy = false;
                    app.IsProgressIndeterminate = false;
                }
            }
            else if (!op.Success && !string.IsNullOrWhiteSpace(op.Message))
                MessageBox.Show(op.Message, _loc.Get("inst.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsInstalling = false;
            IsOverallIndeterminate = false;
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(CanRefreshWinget));
        }
    }
}

public sealed class InstallerCategoryGroup : ObservableObject
{
    public InstallerCategoryGroup(string categoryId, string categoryKey, string title)
    {
        CategoryId = categoryId;
        CategoryKey = categoryKey;
        Title = title;
        Apps = new ObservableCollection<InstallerAppItemViewModel>();
    }

    public string CategoryId { get; }
    public string CategoryKey { get; }
    public string Title { get; set; }
    public ObservableCollection<InstallerAppItemViewModel> Apps { get; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public void ApplyFilter(string? globalQuery, string? localQuery)
    {
        var any = false;
        foreach (var app in Apps)
        {
            app.IsVisible = app.MatchesSearch(globalQuery) && app.MatchesSearch(localQuery);
            if (app.IsVisible)
                any = true;
        }
        IsVisible = any;
    }
}

public partial class InstallerAppItemViewModel : ObservableObject
{
    private readonly InstallerCatalogEntry _entry;
    private readonly Action _onSelectionChanged;

    public InstallerAppItemViewModel(InstallerCatalogEntry entry, LocalizationService loc, Action onSelectionChanged)
    {
        _entry = entry;
        _onSelectionChanged = onSelectionChanged;
        WingetId = entry.WingetId;
        IconGlyph = string.IsNullOrWhiteSpace(entry.IconGlyph) ? "📦" : entry.IconGlyph;
        IconKey = entry.IconKey;
        IconImage = TryLoadIcon(IconKey);
        RefreshLabels(loc);
        SetStatus(WingetInstallStatus.Idle, loc);
    }

    public string Id => _entry.Id;
    public string WingetId { get; }
    public string IconGlyph { get; }
    public string IconKey { get; }
    public ImageSource? IconImage { get; }
    public bool HasIconImage => IconImage is not null;

    private static ImageSource? TryLoadIcon(string iconKey)
    {
        if (string.IsNullOrWhiteSpace(iconKey))
            return null;

        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/InstallerIcons/{iconKey}.png", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private string _statusLabel = "";
    [ObservableProperty] private WingetInstallStatus _status = WingetInstallStatus.Idle;
    public bool CanSelect => !IsBusy;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged();
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSelect));

    public void RefreshLabels(LocalizationService loc)
    {
        Title = loc.Get(_entry.DisplayNameKey);
        Description = loc.Get(_entry.DescriptionKey);
        SetStatus(Status, loc);
    }

    public void SetStatus(WingetInstallStatus status, LocalizationService loc, string? detail = null)
    {
        Status = status;
        StatusLabel = status switch
        {
            WingetInstallStatus.Queued => loc.Get("inst.status.queued"),
            WingetInstallStatus.Starting => loc.Get("inst.status.starting"),
            WingetInstallStatus.Installing => loc.Get("inst.status.installing"),
            WingetInstallStatus.Completed => loc.Get("inst.status.completed"),
            WingetInstallStatus.AlreadyInstalled => loc.Get("inst.status.already"),
            WingetInstallStatus.Failed => string.IsNullOrWhiteSpace(detail)
                ? loc.Get("inst.status.failed")
                : loc.Get("inst.status.failed") + ": " + detail,
            WingetInstallStatus.Cancelled => loc.Get("inst.status.cancelled"),
            _ => loc.Get("inst.status.idle")
        };
    }

    public bool MatchesSearch(string? query) =>
        SearchKeywords.Matches(query, Title, Description, WingetId, _entry.Category);
}

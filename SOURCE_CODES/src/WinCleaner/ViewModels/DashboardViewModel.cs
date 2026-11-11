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
using WinCleaner.Core.Operations;
using WinCleaner.Core.Services;
using WinCleaner.Converters;
using WinCleaner.Data;
using WinCleaner.Services;
using WinCleaner.Views;

namespace WinCleaner.ViewModels;

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
        _app.Snapshots.Changed += OnChangeLogChanged;
        _app.RestorePoints.SessionCheckpointChanged += OnRestoreChanged;
        CpuName = app.Hardware.CpuName;
        GpuName = app.Hardware.GpuName;
        RamText = loc.FormatBytes(app.Hardware.TotalRamBytes);
        OsText = app.Hardware.OsDisplayName;
        DiskText = string.Join(" | ", app.Hardware.Disks.Select(d =>
            string.Format(CultureInfo.CurrentCulture, loc.Get("dash.diskFreeOf"),
                d.Name, loc.FormatBytes(d.FreeBytes), loc.FormatBytes(d.TotalBytes))));
    }

    private void OnChangeLogChanged() => UiThread.Run(() =>
    {
        RefreshRecent();
        RefreshTimeMachine();
    });
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
    [ObservableProperty] private string _applyGamerDesc = "";
    [ObservableProperty] private string _flushDnsDesc = "";
    [ObservableProperty] private string _cleanTempDesc = "";
    [ObservableProperty] private string _systemTitle = "";
    [ObservableProperty] private string _safetyTitle = "";
    [ObservableProperty] private string _cpuLabel = "";
    [ObservableProperty] private string _gpuLabel = "";
    [ObservableProperty] private string _ramLabel = "";
    [ObservableProperty] private string _osLabel = "";
    [ObservableProperty] private string _disksLabel = "";
    [ObservableProperty] private string _timeMachineTitle = "";
    [ObservableProperty] private string _timeMachineStatus = "";
    [ObservableProperty] private string _timeMachineRevertLabel = "";
    [ObservableProperty] private string _timeMachineHistoryLabel = "";
    [ObservableProperty] private bool _hasSnapshot;
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<RecentChangeRow> RecentChanges { get; } = new();
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
        ApplyGamerDesc = _loc.Get("dash.quick.gamerDesc");
        FlushDnsDesc = _loc.Get("dash.quick.dnsDesc");
        CleanTempDesc = _loc.Get("dash.quick.tempDesc");
        SystemTitle = _loc.Get("dash.system");
        SafetyTitle = _loc.Get("dash.safety");
        CpuLabel = _loc.Get("dash.cpu");
        GpuLabel = _loc.Get("dash.gpu");
        RamLabel = _loc.Get("dash.ram");
        OsLabel = _loc.Get("dash.os");
        DisksLabel = _loc.Get("dash.disks");
        TimeMachineTitle = _loc.Get("tm.title");
        TimeMachineRevertLabel = _loc.Get("tm.revertLatest");
        TimeMachineHistoryLabel = _loc.Get("tm.history");
        RefreshRestoreStatus();
        RefreshTimeMachine();
    }

    public void RefreshRestoreStatus()
    {
        RestoreStatusText = !_app.RestorePoints.HasSessionCheckpoint
            ? _loc.Get("restore.statusNone")
            : RestorePointManager.LastOutcome == RestorePointOutcome.AlreadyExists
                ? _loc.Get("restore.existing")
                : _loc.Get("restore.statusOk");
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunQuickActions));

    public void RefreshRecent()
    {
        RecentChanges.Clear();
        foreach (var e in _app.ChangeLog.SessionEntries.Reverse().Take(15))
        {
            var status = e.Success ? _loc.Get("common.statusOk") : _loc.Get("common.statusFail");
            var actionId = e.ActionId;
            var canRevert = e.CanRevert && e.Success;
            RecentChanges.Add(new RecentChangeRow
            {
                Text = $"{e.Timestamp:HH:mm:ss} — {e.DisplayName} ({status})",
                CanRevert = canRevert,
                RevertLabel = _loc.Get("tm.revertItem"),
                RevertCommand = canRevert
                    ? new AsyncRelayCommand(async () =>
                    {
                        await _main.RevertJournalEntryAsync(actionId).ConfigureAwait(true);
                        RefreshRecent();
                        RefreshTimeMachine();
                    })
                    : null
            });
        }
        OnPropertyChanged(nameof(HasRecentChanges));
    }

    public void RefreshTimeMachine()
    {
        var snap = _app.Snapshots.GetLatest();
        HasSnapshot = snap is not null;
        TimeMachineStatus = snap is null
            ? _loc.Get("tm.none")
            : $"{snap.CreatedAt:HH:mm} — {snap.Name} ({snap.Items.Count})";
    }

    [RelayCommand]
    private async Task RevertLatestSnapshotAsync()
    {
        var snap = _app.Snapshots.GetLatest();
        if (snap is null) return;
        var confirm = MessageBox.Show(
            _loc.Format("tm.confirmRevertSnapshot", snap.Name, snap.RevertableCount),
            _loc.Get("tm.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;
        await _main.RevertSnapshotAsync(snap).ConfigureAwait(true);
        RefreshRecent();
        RefreshTimeMachine();
        _main.StatusText = _loc.Get("tm.reverted");
    }

    [RelayCommand]
    private void OpenTimeMachine() => _main.OpenTimeMachine();

    [RelayCommand]
    private async Task CreateRestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _main.ShowCreatingRestoreDialogAsync(Application.Current?.MainWindow).ConfigureAwait(true);
            RefreshRestoreStatus();
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
        RefreshTimeMachine();
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        if (IsBusy) return;
        var tweak = CatalogLoader.LoadTweaks().First(t => t.Id == "tweak-dns-flush");
        var action = _app.ActionFactory.CreateFromTweak(tweak);
        if (action is not null)
            await OperationHost.TrackAsync(_app.Operations, _loc.Get("dash.flushDns"), token =>
                _app.RunActionAsync(_loc.Get("dash.flushDns"), action, false, token));
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
            long freed = 0;
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("dash.cleanTemp"), token =>
                _app.RunWorkAsync(_loc.Get("dash.cleanTemp"), true, async ctx =>
                {
                    var uiProgress = new Progress<string>(raw =>
                    {
                        if (TryParseCleanupProgress(raw, out var pct, out var path))
                            ctx.Progress.Report(new OperationStep(string.IsNullOrWhiteSpace(path) ? raw : path, pct));
                        else
                            ctx.Progress.Report(new OperationStep(raw, null));
                    });
                    freed = await Task.Run(() => _app.TempCleanup.CleanTempFolders(uiProgress, ctx.Token), ctx.Token)
                        .ConfigureAwait(true);
                }, token)).ConfigureAwait(true);

            if (!op.Success)
            {
                if (!op.Cancelled)
                    MessageBox.Show(op.Message ?? _loc.Get("msg.failed"), _loc.Get("app.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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

        await Task.CompletedTask.ConfigureAwait(true);
    }

    private static bool TryParseCleanupProgress(string raw, out double percent, out string path)
    {
        percent = 0;
        path = raw;
        var pipe = raw.IndexOf('|');
        if (pipe <= 0)
            return false;
        if (!double.TryParse(raw.AsSpan(0, pipe), out percent))
            return false;
        path = raw[(pipe + 1)..];
        return true;
    }
}

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
    [RelayCommand]
    private void Navigate(string? page)
    {
        SelectedNav = page ?? "dashboard";
    }

    /// <summary>Open a category from search results while keeping the active keyword filter.</summary>
    public void NavigateFromSearch(string navKey)
    {
        _keepSearchOnNav = true;
        try
        {
            if (string.Equals(SelectedNav, navKey, StringComparison.OrdinalIgnoreCase))
            {
                CurrentPage = ResolvePage(navKey);
                if (!string.IsNullOrWhiteSpace(SearchText))
                    ApplyPageFilters(SearchText);
            }
            else
            {
                SelectedNav = navKey;
            }
        }
        finally
        {
            _keepSearchOnNav = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ApplyPageFilters(null);
            CurrentPage = ResolvePage(SelectedNav);
            if (CurrentPage is DashboardViewModel dash)
                dash.RefreshRecent();
            return;
        }

        ApplyPageFilters(value);
        GlobalSearch.Rebuild(value);
        CurrentPage = GlobalSearch;
    }

    private void ApplyPageFilters(string? query)
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
        Presets.Filter(query);
    }

    [RelayCommand]
    private async Task UndoAllAsync()
    {
        var snap = App.Snapshots.GetLatest();
        if (snap is not null)
        {
            var confirmSnap = MessageBox.Show(
                _loc.Format("tm.confirmRevertSnapshot", snap.Name, snap.RevertableCount),
                _loc.Get("tm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmSnap != MessageBoxResult.Yes) return;

            await RevertSnapshotAsync(snap).ConfigureAwait(true);
            StatusText = _loc.Get("msg.undoCompleted");
            Dashboard.RefreshRecent();
            Dashboard.RefreshTimeMachine();
            return;
        }

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
            await OperationHost.TrackAsync(App.Operations, action.DisplayName, token =>
                App.RunActionAsync(action.DisplayName, action, isRevert: true, token));
        }

        StatusText = _loc.Get("msg.undoCompleted");
        Dashboard.RefreshRecent();
        Dashboard.RefreshTimeMachine();
    }

    public async Task<bool> RevertSnapshotAsync(ChangeSnapshot snapshot)
    {
        var ok = true;
        foreach (var item in snapshot.Items.Where(i => i.CanRevert).Reverse())
        {
            var action = TryResolveAction(item.ActionId);
            if (action is null)
            {
                ok = false;
                continue;
            }

            action.SeedPreviousState(item.OldValue);
            var result = await OperationHost.TrackAsync(App.Operations, action.DisplayName, token =>
                App.RunActionAsync(action.DisplayName, action, isRevert: true, token));
            if (result.Success)
                App.Snapshots.RemoveItem(snapshot.Id, item.ActionId);
            else
                ok = false;
        }

        return ok;
    }

    public async Task<bool> RevertSnapshotItemAsync(ChangeSnapshot snapshot, SnapshotItem item)
    {
        if (!item.CanRevert)
            return false;

        var action = TryResolveAction(item.ActionId);
        if (action is null)
            return false;

        action.SeedPreviousState(item.OldValue);
        var result = await OperationHost.TrackAsync(App.Operations, action.DisplayName, token =>
            App.RunActionAsync(action.DisplayName, action, isRevert: true, token)).ConfigureAwait(true);
        if (result.Success)
            App.Snapshots.RemoveItem(snapshot.Id, item.ActionId);
        return result.Success;
    }

    public async Task RevertJournalEntryAsync(string actionId)
    {
        var entry = App.ChangeLog.GetLatestRevertable(actionId);
        if (entry is null)
            return;

        var action = TryResolveAction(actionId);
        if (action is null)
            return;

        action.SeedPreviousState(entry.OldValue);
        await OperationHost.TrackAsync(App.Operations, action.DisplayName, token =>
            App.RunActionAsync(action.DisplayName, action, isRevert: true, token)).ConfigureAwait(true);
        var snap = App.Snapshots.GetAll()
            .FirstOrDefault(s => s.Items.Any(i => string.Equals(i.ActionId, actionId, StringComparison.OrdinalIgnoreCase)));
        if (snap is not null)
            App.Snapshots.RemoveItem(snap.Id, actionId);
    }

    public IChangeAction? TryResolveAction(string actionId)
    {
        var tweak = CatalogLoader.LoadTweaks().FirstOrDefault(t => t.Id == actionId);
        if (tweak is not null) return App.ActionFactory.CreateFromTweak(tweak);

        var svc = CatalogLoader.LoadServices().FirstOrDefault(s => s.Id == actionId);
        if (svc is not null)
            return App.ActionFactory.CreateServiceAction(svc.Id, svc.ServiceName, svc.DescriptionKey, svc.Category,
                RiskParser.Parse(svc.Risk), svc.ServiceName, svc.RecommendedStart, svc.ServiceAliases);

        if (string.Equals(actionId, DnsPresetChangeAction.ActionId, StringComparison.OrdinalIgnoreCase))
            return new DnsPresetChangeAction(App.Dns, App.Dns.DetectCurrentPreset(), key => _loc.Get(key));

        if (string.Equals(actionId, TakeOwnershipChangeAction.ActionId, StringComparison.OrdinalIgnoreCase))
            return new TakeOwnershipChangeAction(App.SystemHealth, desiredEnabled: false, _loc.Get("tools.takeOwn.menu"));

        if (StartupToggleAction.TryParseId(actionId, out var hive, out var name))
        {
            var live = App.Startup.GetStartupEntries()
                .FirstOrDefault(e => e.Hive.Equals(hive, StringComparison.OrdinalIgnoreCase)
                                     && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var entry = live ?? new StartupEntry { Name = name, Command = "", Hive = hive, IsEnabled = false };
            return new StartupToggleAction(App.Startup, entry, desiredEnabled: true);
        }

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

        var batch = ActionPreviewBuilder.Build(actions);
        if (batch.ChangeCount == 0)
        {
            MessageBox.Show(_loc.Get("preview.nothingToChange"), title);
            return;
        }

        var dlg = new ApplyPreviewDialog(batch, title, _loc)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true)
            return;

        var progress = new Progress<string>(s => StatusText = s);
        var op = await OperationHost.TrackAsync(App.Operations, title, token =>
        {
            progress.ProgressChanged += (_, text) => StatusText = text;
            return App.RunActionsAsync(title, batch.ToApply, title, token, antiCheatConfirmed: batch.HasAntiCheat);
        });
        if (!op.Success && !op.Cancelled && !string.IsNullOrWhiteSpace(op.Message))
        {
            MessageBox.Show(_loc.Get(op.Message), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        StatusText = _loc.Get(op.Success ? "msg.done" : "msg.failed");
        Dashboard.RefreshRecent();
        Dashboard.RefreshTimeMachine();
    }

    public void OpenTimeMachine()
    {
        var dlg = new TimeMachineDialog(
            App.Snapshots.GetAll(),
            _loc,
            async snap =>
            {
                var confirm = MessageBox.Show(
                    _loc.Format("tm.confirmRevertSnapshot", snap.Name, snap.RevertableCount),
                    _loc.Get("tm.title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                    return false;
                var ok = await RevertSnapshotAsync(snap).ConfigureAwait(true);
                Dashboard.RefreshRecent();
                Dashboard.RefreshTimeMachine();
                StatusText = _loc.Get("tm.reverted");
                return ok;
            },
            async (snap, item) =>
            {
                var confirm = MessageBox.Show(
                    _loc.Format("tm.confirmRevertItem", item.DisplayName),
                    _loc.Get("tm.title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                    return false;
                var ok = await RevertSnapshotItemAsync(snap, item).ConfigureAwait(true);
                Dashboard.RefreshRecent();
                Dashboard.RefreshTimeMachine();
                StatusText = _loc.Get("tm.reverted");
                return ok;
            })
        {
            Owner = Application.Current?.MainWindow
        };
        dlg.ShowDialog();
        Dashboard.RefreshRecent();
        Dashboard.RefreshTimeMachine();
    }

    /// <summary>
    /// Shows the existing confirm dialogs when a gate asks. Preview already records anti-cheat
    /// acknowledgement, so that path does not reach this service.
    /// </summary>
    private sealed class UiGateConfirmation : IConfirmationService
    {
        private readonly LocalizationService _loc;

        public UiGateConfirmation(LocalizationService loc) => _loc = loc;

        public Task<bool> ConfirmAsync(string code, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(false);

            return Task.FromResult(UiThread.Invoke(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                return code switch
                {
                    GateCodes.AntiCheat => Show("danger.anticheat.title", "danger.anticheat.message"),
                    GateCodes.Arm64 => Show("danger.arm64.title", "danger.arm64.message"),
                    _ => false
                };
            }));
        }

        private bool Show(string titleKey, string messageKey)
        {
            var dlg = new ConfirmDangerDialog(
                title: _loc.Get(titleKey),
                message: _loc.Get(messageKey),
                acknowledgeLabel: _loc.Get("danger.ack"))
            {
                Owner = Application.Current?.MainWindow
            };
            return dlg.ShowDialog() == true;
        }
    }
}

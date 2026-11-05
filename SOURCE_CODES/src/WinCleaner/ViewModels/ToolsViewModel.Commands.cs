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
using WinCleaner.Core.Operations;
using WinCleaner.Core.Services;
using WinCleaner.Converters;
using WinCleaner.Data;
using WinCleaner.Services;
using WinCleaner.Views;

namespace WinCleaner.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    [RelayCommand]
    private async Task CreateRestoreAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (Application.Current?.MainWindow?.DataContext is MainViewModel main)
                await main.ShowCreatingRestoreDialogAsync(Application.Current.MainWindow).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CleanTempAsync()
    {
        if (IsBusy) return;
        if (MessageBox.Show(
                _loc.Get("tools.cleanTemp.confirm"),
                _loc.Get("tools.cleanTemp"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        try
        {
            long freed = 0;
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("tools.cleanTemp"), token =>
                _app.RunWorkAsync(_loc.Get("tools.cleanTemp"), true, async ctx =>
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
                    MessageBox.Show(op.Message ?? _loc.Get("msg.failed"), "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(
                string.Format(_loc.Get("tools.tempFreed"), _loc.FormatBytes(freed)),
                "WinCleaner");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (!await EnsureDangerousToolAsync().ConfigureAwait(true)) return;

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
        if (!await EnsureDangerousToolAsync().ConfigureAwait(true)) return;

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
        if (!await EnsureDangerousToolAsync().ConfigureAwait(true)) return;

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
    private async Task ToggleTakeOwnershipAsync()
    {
        var desired = !TakeOwnershipEnabled;
        try
        {
            if (!PrivilegeHelper.IsAdministrator())
            {
                MessageBox.Show(_loc.Get("common.adminRequired"), "WinCleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var action = new TakeOwnershipChangeAction(
                _app.SystemHealth, desired, _loc.Get("tools.takeOwn.menu"));
            if (_previewApply is not null)
            {
                await _previewApply([action], _loc.Get("tools.takeOwn.name")).ConfigureAwait(true);
                TakeOwnershipEnabled = _app.SystemHealth.IsTakeOwnershipMenuEnabled();
                return;
            }

            await OperationHost.TrackAsync(_app.Operations, _loc.Get("tools.takeOwn.name"), token =>
                _app.RunActionAsync(_loc.Get("tools.takeOwn.name"), action, false, token)).ConfigureAwait(true);
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

    [RelayCommand]
    private async Task ConfigureNtpPoolAsync()
    {
        if (IsBusy) return;
        if (MessageBox.Show(_loc.Get("tools.ntp.confirm"), _loc.Get("tools.ntp"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        await RunWithProgressWindowAsync(
            _loc.Get("tools.ntp"),
            (progress, ct) => _app.SystemHealth.ConfigureNtpPoolAsync(progress, ct)).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenAutologon()
    {
        try
        {
            // Safer than downloading Sysinternals Autologon.exe: classic netplwiz UI.
            SystemHealthService.LaunchLegacyPanel("netplwiz.exe");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("tools.autologon"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task RepairWingetAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("tools.wingetRepair"), token =>
                _app.RunWorkAsync(_loc.Get("tools.wingetRepair"), true, async ctx =>
                {
                    ctx.Progress.Report(new OperationStep(_loc.Get("tools.wingetRepair"), null));
                    var result = await _app.Winget.TryBootstrapWingetAsync(ctx.Token).ConfigureAwait(true);
                    if (!result.Success)
                        throw new InvalidOperationException(result.Message ?? _loc.Get("inst.wingetBootstrapFail"));
                }, token)).ConfigureAwait(true);

            MessageBox.Show(
                op.Success ? _loc.Get("inst.wingetBootstrapOk") : string.Format(_loc.Get("inst.wingetBootstrapFail"), op.Message),
                _loc.Get("tools.wingetRepair"),
                MessageBoxButton.OK,
                op.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (!op.Success && !op.Cancelled)
                _app.Winget.OpenAppInstallerStore();
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ExportRegistryAsync()
    {
        if (IsBusy) return;
        var dialog = new SaveFileDialog
        {
            Filter = "Registry (*.reg)|*.reg",
            FileName = "WinCleaner-settings.reg",
            DefaultExt = ".reg",
            AddExtension = true
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        try
        {
            var path = dialog.FileName;
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("tools.regExport"), token =>
                _app.RunWorkAsync(_loc.Get("tools.regExport"), true, async ctx =>
                {
                    ctx.Progress.Report(new OperationStep(_loc.Get("tools.regExport"), null));
                    var result = await _app.RegistryTransfer.ExportAsync(@"HKCU\Software\WinCleaner", path, ctx.Token)
                        .ConfigureAwait(false);
                    if (result.TimedOut || result.Cancelled || result.ExitCode is not 0)
                        throw new InvalidOperationException(result.Stderr.Length > 0 ? result.Stderr : _loc.Get("msg.failed"));
                }, token)).ConfigureAwait(true);

            if (op.Success)
                MessageBox.Show(_loc.Get("tools.regExport.ok"), _loc.Get("tools.regExport"), MessageBoxButton.OK, MessageBoxImage.Information);
            else if (!op.Cancelled)
                MessageBox.Show(op.Message ?? _loc.Get("msg.failed"), _loc.Get("tools.regExport"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportRegistryAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Registry (*.reg)|*.reg",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;

        if (MessageBox.Show(
                _loc.Get("tools.regImport.confirm"),
                _loc.Get("tools.regImport"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        IsBusy = true;
        try
        {
            var path = dialog.FileName;
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("tools.regImport"), token =>
                _app.RunWorkAsync(_loc.Get("tools.regImport"), true, async ctx =>
                {
                    ctx.Progress.Report(new OperationStep(_loc.Get("tools.regImport"), null));
                    var result = await _app.RegistryTransfer.ImportAsync(path, ctx.Token).ConfigureAwait(false);
                    if (result.TimedOut || result.Cancelled || result.ExitCode is not 0)
                        throw new InvalidOperationException(result.Stderr.Length > 0 ? result.Stderr : _loc.Get("msg.failed"));
                }, token)).ConfigureAwait(true);

            if (op.Success)
                MessageBox.Show(_loc.Get("tools.regImport.ok"), _loc.Get("tools.regImport"), MessageBoxButton.OK, MessageBoxImage.Information);
            else if (!op.Cancelled)
                MessageBox.Show(op.Message ?? _loc.Get("msg.failed"), _loc.Get("tools.regImport"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenLegacyPanel(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            SystemHealthService.LaunchLegacyPanel(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, _loc.Get("tools.legacy"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task<bool> EnsureDangerousToolAsync()
    {
        if (!_app.Settings.AllowDangerousActions)
        {
            MessageBox.Show(
                _loc.Get("danger.needSettings"),
                _loc.Get("danger.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return await _confirm().ConfigureAwait(true);
    }

    private async Task RunWithProgressWindowAsync(
        string title,
        Func<IProgress<string>, CancellationToken, Task> work)
    {
        IsBusy = true;
        try
        {
            var op = await OperationHost.TrackAsync(_app.Operations, title, token =>
                _app.RunWorkAsync(title, true, async ctx =>
                {
                    var progress = new Progress<string>(line => ctx.Progress.Report(new OperationStep(line, null)));
                    await work(progress, ctx.Token).ConfigureAwait(false);
                }, token)).ConfigureAwait(true);
            if (!op.Success && !op.Cancelled && !string.IsNullOrWhiteSpace(op.Message))
                MessageBox.Show(op.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

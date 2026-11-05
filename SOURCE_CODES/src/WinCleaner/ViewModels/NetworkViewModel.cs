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

public partial class NetworkViewModel : TweaksCategoryViewModel
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;

    public NetworkViewModel(AppServices app, LocalizationService loc, Func<Task<bool>> confirm,
        Func<Task<bool>>? confirmAntiCheat = null,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
        : base("Network", "net.title", "net.subtitle", app, loc, confirm, confirmAntiCheat, previewApply)
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
    [ObservableProperty] private string _customDns = "";
    [ObservableProperty] private string _adapterFilter = "";
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
    private async Task MeasureDnsLatencyAsync()
    {
        var host = DnsChangerService.ParseCustomServers(CustomDns).FirstOrDefault()
                   ?? DnsChangerService.Presets.First(p => p.Kind == DnsPresetKind.Cloudflare).Primary
                   ?? "1.1.1.1";
        long? ms = null;
        await _app.RunWorkAsync(_loc.Get("net.dns.latency"), false, async ctx =>
        {
            ms = await _app.Dns.MeasureLatencyAsync(host, ctx.Token).ConfigureAwait(false);
        }).ConfigureAwait(true);
        DnsStatus = ms is null
            ? _loc.Get("net.dns.latencyFail")
            : string.Format(_loc.Get("net.dns.latencyOk"), host, ms);
    }

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (IsBusy) return;
        if (!string.IsNullOrWhiteSpace(CustomDns))
        {
            IsBusy = true;
            DnsStatus = _loc.Get("net.dns.applying");
            try
            {
                DnsApplyResult? applied = null;
                await _app.RunWorkAsync(_loc.Get("net.dns.title"), true, ctx =>
                {
                    applied = _app.Dns.ApplyCustom(CustomDns, AdapterFilter);
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                DnsStatus = applied?.Message ?? _loc.Get("net.dns.latencyFail");
            }
            finally
            {
                IsBusy = false;
            }
            return;
        }

        if (SelectedDnsOption is null) return;
        IsBusy = true;
        DnsStatus = _loc.Get("net.dns.applying");
        try
        {
            var kind = SelectedDnsOption.Kind;
            var action = new DnsPresetChangeAction(_app.Dns, kind, key => _loc.Get(key));
            if (_previewApply is not null)
            {
                await _previewApply([action], _loc.Get("net.dns.title")).ConfigureAwait(true);
                SelectDetectedDns();
                return;
            }

            var result = await OperationHost.TrackAsync(_app.Operations, _loc.Get("net.dns.title"), token =>
                _app.RunActionAsync(_loc.Get("net.dns.title"), action, false, token)).ConfigureAwait(true);
            DnsStatus = result.Success
                ? string.Format(_loc.Get("net.dns.applied"), SelectedDnsOption.DisplayName, 0)
                : (_loc.Get("common.adminRequired") + " " + (result.Message ?? ""));

            if (!result.Success)
                MessageBox.Show(result.Message ?? "", _loc.Get("net.dns.title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            SelectDetectedDns();
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
            (bool Success, string? ErrorMessage) flush = default;
            var op = await OperationHost.TrackAsync(_app.Operations, _loc.Get("net.dns.flush"), token =>
                _app.RunWorkAsync(_loc.Get("net.dns.flush"), true, async ctx =>
                {
                    ctx.Progress.Report(new OperationStep(_loc.Get("net.dns.flush"), null));
                    flush = await Task.Run(() => _app.Dns.FlushDnsCache(), ctx.Token).ConfigureAwait(true);
                    if (!flush.Success)
                        throw new InvalidOperationException(flush.ErrorMessage ?? _loc.Get("net.dns.flushFail"));
                }, token)).ConfigureAwait(true);

            DnsStatus = op.Success
                ? _loc.Get("net.dns.flushOk")
                : (op.Cancelled ? _loc.Get("net.dns.flushFail") : (op.Message ?? _loc.Get("net.dns.flushFail")));
            if (!op.Success && !op.Cancelled)
                MessageBox.Show(DnsStatus, _loc.Get("net.dns.flush"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }
}

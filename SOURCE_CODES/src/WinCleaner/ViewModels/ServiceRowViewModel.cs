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

public partial class ServiceRowViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly ServiceCatalogEntry _entry;
    private readonly LocalizationService _loc;
    private readonly Func<Task<bool>> _confirm;
    private readonly Func<IReadOnlyList<IChangeAction>, string, Task>? _previewApply;
    private int _applyGate;

    public ServiceRowViewModel(
        ServiceCatalogEntry entry,
        WindowsServiceInfo? info,
        AppServices app,
        LocalizationService loc,
        Func<Task<bool>> confirm,
        Func<IReadOnlyList<IChangeAction>, string, Task>? previewApply = null)
    {
        _entry = entry;
        _app = app;
        _loc = loc;
        _confirm = confirm;
        _previewApply = previewApply;
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

    public bool MatchesSearch(string? query) =>
        SearchKeywords.Matches(query, Title, Description, ServiceName, Category, Id);

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
            if (desiredDisabled && _previewApply is not null
                && (Risk is RiskLevel.Caution or RiskLevel.Dangerous))
            {
                IsDisabled = previous;
                await _previewApply([CreateDisableAction()], Title).ConfigureAwait(true);
                ApplyLiveInfo(_app.Services.GetService(ServiceName));
                return;
            }

            if (desiredDisabled && Risk == RiskLevel.Dangerous && !await _confirm().ConfigureAwait(true))
            {
                IsDisabled = previous;
                return;
            }

            OperationResult result;
            if (desiredDisabled)
            {
                var action = CreateDisableAction();
                result = await OperationHost.TrackAsync(_app.Operations, Title, token =>
                    _app.RunActionAsync(Title, action, false, token)).ConfigureAwait(true);
            }
            else
            {
                var journal = _app.ChangeLog.GetLatestRevertable(Id);
                if (journal is not null)
                {
                    var action = CreateDisableAction();
                    action.SeedPreviousState(journal.OldValue);
                    result = await OperationHost.TrackAsync(_app.Operations, Title, token =>
                        _app.RunActionAsync(Title, action, true, token)).ConfigureAwait(true);
                }
                else
                {
                    var action = _app.ActionFactory.CreateServiceAction(
                        _entry.Id, Title, Description, Category, Risk, ServiceName, "Manual");
                    result = await OperationHost.TrackAsync(_app.Operations, Title, token =>
                        _app.RunActionAsync(Title, action, false, token)).ConfigureAwait(true);
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

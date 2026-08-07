using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Services;

namespace WinCleaner.ViewModels;

public partial class TweakItemViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly LocalizationService _loc;
    private readonly string _titleKey;
    private readonly string _descriptionKey;
    private readonly Func<IChangeAction> _createAction;
    private readonly Func<Task<bool>>? _confirmDangerous;
    private readonly Func<Task<bool>>? _confirmAntiCheat;
    private int _applyGate; // 0 = idle, 1 = busy (Interlocked)

    public TweakItemViewModel(
        string id,
        string titleKey,
        string descriptionKey,
        string category,
        RiskLevel risk,
        AppServices app,
        LocalizationService loc,
        Func<IChangeAction> createAction,
        bool isApplied = false,
        bool isEnabled = true,
        Func<Task<bool>>? confirmDangerous = null,
        bool requiresAntiCheatConfirm = false,
        Func<Task<bool>>? confirmAntiCheat = null)
    {
        Id = id;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
        Category = category;
        Risk = risk;
        RequiresAntiCheatConfirm = requiresAntiCheatConfirm;
        _app = app;
        _loc = loc;
        _createAction = createAction;
        _isApplied = isApplied;
        IsEnabled = isEnabled && risk != RiskLevel.Blocked;
        _confirmDangerous = confirmDangerous;
        _confirmAntiCheat = confirmAntiCheat;
        RefreshLabels();
    }

    public string Id { get; }
    public string Category { get; }
    public RiskLevel Risk { get; }
    public bool RequiresAntiCheatConfirm { get; }
    public bool IsEnabled { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _riskLabel = "";
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public void RefreshLabels()
    {
        Title = _loc.Get(_titleKey);
        Description = _loc.Get(_descriptionKey);
        RiskLabel = Risk switch
        {
            RiskLevel.Blocked => _loc.Get("common.risk.blocked"),
            RiskLevel.Dangerous => _loc.Get("common.risk.dangerous"),
            RiskLevel.Caution => _loc.Get("common.risk.caution"),
            _ => _loc.Get("common.risk.safe")
        };
    }

    public bool MatchesSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               Id.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public void SetAppliedSilent(bool value) => IsApplied = value;

    /// <summary>
    /// Explicit command — do NOT apply from property-changed / TwoWay binding.
    /// DataTemplate load was writing false back into IsApplied and triggering system changes on tab switch.
    /// </summary>
    [RelayCommand]
    private async Task ToggleAsync()
    {
        if (!IsEnabled || Risk == RiskLevel.Blocked) return;
        if (Interlocked.Exchange(ref _applyGate, 1) == 1) return;

        var desired = !IsApplied;
        var previous = IsApplied;
        IsBusy = true;
        IsApplied = desired; // optimistic UI update (OneWay binding refreshes)

        try
        {
            if (desired && RequiresAntiCheatConfirm && _confirmAntiCheat is not null)
            {
                if (!await _confirmAntiCheat().ConfigureAwait(true))
                {
                    IsApplied = previous;
                    return;
                }
            }
            else if (desired && Risk == RiskLevel.Dangerous && _confirmDangerous is not null)
            {
                if (!await _confirmDangerous().ConfigureAwait(true))
                {
                    IsApplied = previous;
                    return;
                }
            }

            var action = _createAction();
            if (!desired)
            {
                var journal = _app.ChangeLog.GetLatestRevertable(Id);
                action.SeedPreviousState(journal?.OldValue);
            }

            var result = desired
                ? await _app.Executor.ExecuteAsync(action).ConfigureAwait(true)
                : await _app.Executor.ExecuteAsync(action, isRevert: true).ConfigureAwait(true);

            StatusMessage = result.Message;
            if (!result.Success)
            {
                IsApplied = previous;
                ShowMessage(result.Message ?? _loc.Get("msg.failed"), MessageBoxImage.Warning);
            }
            else if (desired && action.RequiresReboot)
            {
                ShowMessage(_loc.Get("common.rebootRequired"), MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            IsApplied = previous;
            ShowMessage(ex.Message, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Interlocked.Exchange(ref _applyGate, 0);
        }
    }

    private void ShowMessage(string message, MessageBoxImage icon)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        var title = _loc.Get("app.title");
        if (dispatcher.CheckAccess())
            MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        else
            dispatcher.Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, icon));
    }
}

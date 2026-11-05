using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class PowerPlanChangeAction : IChangeAction
{
    private readonly PowerPlanManager _power;
    private string? _previousScheme;

    public PowerPlanChangeAction(string id, string displayName, string description, RiskLevel risk, PowerPlanManager power)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Risk = risk;
        Category = "Gaming";
        _power = power;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_power.GetActiveScheme());

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _previousScheme = _power.GetActiveScheme();
        _power.ActivateUltimatePerformance();
        return Task.FromResult(ChangeResult.Ok(_previousScheme, "Ultimate Performance", "Power plan activated"));
    }

    public bool IsApplied() => _power.IsUltimateActive();

    public ActionPreview BuildPreview()
    {
        var current = _power.GetActiveScheme() ?? "unknown";
        const string proposed = "Ultimate Performance";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = "powercfg:active-scheme",
                    Current = current,
                    Proposed = proposed,
                    Kind = PreviewDiffKind.Set,
                    Unchanged = IsApplied()
                }
            ]);
    }

    public void SeedPreviousState(string? oldValue) => _previousScheme = oldValue;

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_previousScheme))
            _power.ActivateScheme(_previousScheme);
        else
            _power.ActivateHighPerformance();
        return Task.FromResult(ChangeResult.Ok(null, _previousScheme, "Power plan reverted"));
    }
}

using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

/// <summary>
/// Window animations / best-performance visuals. Must use SPI + UserPreferencesMask;
/// registry + Explorer restart alone does not apply for the current session.
/// </summary>
public sealed class VisualEffectsChangeAction : IChangeAction
{
    public enum Mode
    {
        BestPerformance,
        DisableAnimations
    }

    private readonly RegistryManager _registry;
    private readonly Mode _mode;
    private string? _previousPacked;

    public VisualEffectsChangeAction(
        string id,
        string displayName,
        string description,
        string category,
        RiskLevel risk,
        Mode mode,
        RegistryManager registry)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        Risk = risk;
        _mode = mode;
        _registry = registry;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public RiskLevel Risk { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(IsApplied() ? "applied" : "not-applied");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var mask = VisualEffectsApplier.GetUserPreferencesMask();
        var maskB64 = mask is null ? "" : Convert.ToBase64String(mask);
        _previousPacked = maskB64;

        if (_mode == Mode.BestPerformance)
            VisualEffectsApplier.ApplyBestPerformance(_registry);
        else
            VisualEffectsApplier.ApplyDisableAnimations(_registry);

        return Task.FromResult(ChangeResult.Ok(_previousPacked, "applied",
            _mode == Mode.BestPerformance
                ? "Visual effects set to best performance (SPI + UserPreferencesMask)"
                : "Window animations disabled (SPI)"));
    }

    public void SeedPreviousState(string? oldValue) => _previousPacked = oldValue;

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_mode == Mode.BestPerformance)
        {
            byte[]? previousMask = null;
            if (!string.IsNullOrEmpty(_previousPacked))
            {
                try { previousMask = Convert.FromBase64String(_previousPacked); }
                catch { previousMask = null; }
            }

            VisualEffectsApplier.RevertBestPerformance(_registry, previousMask);
        }
        else
        {
            VisualEffectsApplier.RevertAnimations(_registry);
        }

        return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Visual effects restored"));
    }

    public bool IsApplied() =>
        _mode == Mode.BestPerformance
            ? VisualEffectsApplier.IsBestPerformanceApplied(_registry)
            : VisualEffectsApplier.IsAnimationsDisabled(_registry);

    public ActionPreview BuildPreview()
    {
        var fx = _registry.GetValueAsString(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
            "VisualFXSetting") ?? ActionPreviewBuilder.AbsentMarker;
        var minAnimate = _registry.GetValueAsString("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate")
                         ?? ActionPreviewBuilder.AbsentMarker;
        var taskbar = _registry.GetValueAsString(
            "HKCU",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            "TaskbarAnimations") ?? ActionPreviewBuilder.AbsentMarker;

        List<PreviewDiffRow> rows;
        if (_mode == Mode.BestPerformance)
        {
            rows =
            [
                Row(@"HKCU\...\VisualFXSetting", fx, "3"),
                Row(@"HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate", minAnimate, "0"),
                Row(@"HKCU\...\TaskbarAnimations", taskbar, "0")
            ];
        }
        else
        {
            rows =
            [
                Row(@"HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate", minAnimate, "0"),
                Row(@"HKCU\...\TaskbarAnimations", taskbar, "0")
            ];
        }

        return ActionPreviewDefaults.FromAction(this, IsApplied() ? "applied" : "not-applied", "applied", PreviewDiffKind.Set, rows);

        static PreviewDiffRow Row(string target, string current, string proposed) => new()
        {
            Target = target,
            Current = current,
            Proposed = proposed,
            Kind = PreviewDiffKind.Set,
            Unchanged = string.Equals(current, proposed, StringComparison.OrdinalIgnoreCase)
        };
    }
}

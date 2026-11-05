using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

/// <summary>
/// Applies / reverts several registry values as one catalog tweak (e.g. dark mode, inking).
/// </summary>
public sealed class MultiRegistryChangeAction : IChangeAction
{
    private readonly RegistryManager _registry;
    private readonly IReadOnlyList<RegistryTweakSpec> _specs;
    private readonly List<string?> _previousValues = new();
    private readonly bool _restartExplorer;

    public MultiRegistryChangeAction(
        TweakCatalogEntry entry,
        string displayName,
        string description,
        RegistryManager registry)
    {
        Id = entry.Id;
        DisplayName = displayName;
        Description = description;
        Category = entry.Category;
        Risk = RiskParser.Parse(entry.Risk);
        RequiresReboot = entry.RequiresReboot;
        RequiresAntiCheatConfirm = entry.RequiresAntiCheatConfirm;
        Constraints = ActionConstraints.From(entry.Skus, entry.Archs, entry.RequiresFeature);
        _registry = registry;
        _specs = entry.Registries ?? throw new ArgumentException("Missing registries spec");
        if (_specs.Count == 0)
            throw new ArgumentException("registries must contain at least one entry");
        _restartExplorer = _specs.Any(s => s.RestartExplorer);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public RiskLevel Risk { get; }
    public bool CanRevert => true;
    public bool RequiresReboot { get; }
    public bool RequiresAntiCheatConfirm { get; }
    public ActionConstraints Constraints { get; }
    public bool WritesMachine => _specs.Any(spec =>
        spec.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
        spec.Hive.Equals("HKCR", StringComparison.OrdinalIgnoreCase));

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(IsApplied() ? "applied" : "not-applied");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _previousValues.Clear();
        foreach (var spec in _specs)
            _previousValues.Add(_registry.GetValueAsString(spec.Hive, spec.Path, spec.Name));

        var written = 0;
        try
        {
            for (var i = 0; i < _specs.Count; i++)
            {
                var spec = _specs[i];
                if (spec.DeleteValueOnEnable)
                    _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
                else
                {
                    var kind = RegistryManager.ParseKind(spec.ValueKind);
                    var target = spec.ApplyMeansDisable ? spec.DisabledValue : spec.EnabledValue;
                    _registry.SetValue(spec.Hive, spec.Path, spec.Name, RegistryManager.ParseValue(target, kind), kind);
                }

                written++;
            }
        }
        catch (Exception ex)
        {
            RollbackWritten(written);
            return Task.FromResult(ChangeResult.FailCode(GateCodes.Partial, ex.Message));
        }

        MaybeRestartExplorer();
        // Encode per-value priors so SeedPreviousState can restore after restart.
        var packed = string.Join("\u001f", _previousValues.Select(v => v ?? "\u0000"));
        return Task.FromResult(ChangeResult.Ok(packed, "applied", $"Applied {_specs.Count} registry values"));
    }

    private void RollbackWritten(int written)
    {
        for (var i = written - 1; i >= 0; i--)
        {
            var spec = _specs[i];
            var prior = _previousValues[i];
            try
            {
                if (prior is null)
                    _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
                else
                {
                    var kind = RegistryManager.ParseKind(spec.ValueKind);
                    _registry.SetValue(spec.Hive, spec.Path, spec.Name, RegistryManager.ParseValue(prior, kind), kind);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Registry rollback failed for {spec.Path}: {ex.Message}");
            }
        }
    }

    public void SeedPreviousState(string? oldValue)
    {
        _previousValues.Clear();
        if (string.IsNullOrEmpty(oldValue))
            return;

        // Legacy journal entries only stored "not-applied".
        if (oldValue is "not-applied" or "applied")
            return;

        foreach (var part in oldValue.Split('\u001f'))
            _previousValues.Add(part == "\u0000" ? null : part);
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_previousValues.Count == 0)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        for (var i = 0; i < _specs.Count; i++)
        {
            var spec = _specs[i];
            var previous = i < _previousValues.Count ? _previousValues[i] : null;
            var kind = RegistryManager.ParseKind(spec.ValueKind);

            if (spec.DeleteValueOnDisable && previous is null)
            {
                _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
                continue;
            }

            if (previous is not null)
            {
                var val = RegistryManager.ParseValue(previous, kind);
                _registry.SetValue(spec.Hive, spec.Path, spec.Name, val, kind);
            }
            else if (spec.DeleteValueOnDisable || spec.DeleteValueOnEnable)
            {
                _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
            }
            else
            {
                return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));
            }
        }

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Registry values reverted"));
    }

    public ActionPreview BuildPreview()
    {
        var rows = _specs.Select(s => ActionPreviewBuilder.RegistryRow(_registry, s)).ToList();
        var current = IsApplied() ? "applied" : "not-applied";
        return ActionPreviewDefaults.FromAction(this, current, "applied", PreviewDiffKind.Set, rows);
    }

    public bool IsApplied()
    {
        foreach (var spec in _specs)
        {
            if (spec.OptionalForDetect)
                continue;

            var current = _registry.GetValueAsString(spec.Hive, spec.Path, spec.Name);
            if (spec.DeleteValueOnEnable)
            {
                if (current is not null)
                    return false;
                continue;
            }

            if (spec.EnabledValue.Length == 0)
            {
                if (current is null || current.Length != 0)
                    return false;
                continue;
            }

            if (!RegistryManager.ValuesEqual(current, spec.EnabledValue, spec.ValueKind))
                return false;
        }

        return true;
    }

    private void MaybeRestartExplorer()
    {
        if (!_restartExplorer)
            return;

        // Theme / file-visibility flags: broadcast is enough (WinUtil "refresh").
        // Taskbar / Start / Widgets chrome: hard restart, but without WM_CLOSE / double explorer.exe.
        var needsHardRestart = _specs.Any(s =>
            s.DeleteKeyOnDisable
            || s.Name is "TaskbarDa" or "TaskbarMn" or "TaskbarAl"
                or "ShowTaskViewButton" or "SearchboxTaskbarMode"
                or "Start_IrisRecommendations" or "AllowNewsAndInterests" or "EnableFeeds"
                or "HideRecommendedSection" or "ShowCopilotButton");

        if (needsHardRestart)
            ExplorerRestarter.Restart();
        else
            ExplorerRestarter.Refresh();
    }
}

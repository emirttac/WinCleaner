using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Abstractions;
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
    private readonly IRegistryAccess _registry;
    private readonly IReadOnlyList<RegistryTweakSpec> _specs;
    private readonly List<string?> _previousValues = new();
    private readonly bool _restartExplorer;
    private bool _seeded;
    private bool _legacyUnknown;

    public MultiRegistryChangeAction(
        TweakCatalogEntry entry,
        string displayName,
        string description,
        RegistryManager registry)
        : this(entry, displayName, description, (IRegistryAccess)registry)
    {
    }

    internal MultiRegistryChangeAction(
        TweakCatalogEntry entry,
        string displayName,
        string description,
        IRegistryAccess registry)
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
        _seeded = true;
        _legacyUnknown = false;
        foreach (var spec in _specs)
            _previousValues.Add(_registry.GetValueAsString(spec.Hive, spec.Path, spec.Name));

        var written = new List<int>();
        try
        {
            for (var i = 0; i < _specs.Count; i++)
            {
                var spec = _specs[i];
                try
                {
                    WriteApplied(spec);
                    written.Add(i);
                }
                catch (Exception ex) when (IsOptionalSkip(spec, ex))
                {
                    System.Diagnostics.Trace.WriteLine($"Optional registry write skipped for {spec.Path}\\{spec.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            RollbackWritten(written);
            return Task.FromResult(ChangeResult.FailCode(GateCodes.Partial, ex.Message));
        }

        if (!RequiredSpecsWritten(written))
        {
            RollbackWritten(written);
            return Task.FromResult(ChangeResult.FailCode(GateCodes.Partial, "Required registry value could not be written."));
        }

        MaybeRestartExplorer();
        // Encode per-value priors so SeedPreviousState can restore after restart.
        var packed = string.Join("\u001f", _previousValues.Select(v => v ?? "\u0000"));
        return Task.FromResult(ChangeResult.Ok(packed, "applied", $"Applied {_specs.Count} registry values"));
    }

    private static bool IsOptionalSkip(RegistryTweakSpec spec, Exception ex) =>
        spec.OptionalForDetect && ex is UnauthorizedAccessException or System.Security.SecurityException;

    private bool RequiredSpecsWritten(List<int> written)
    {
        for (var i = 0; i < _specs.Count; i++)
        {
            if (_specs[i].OptionalForDetect)
                continue;
            if (!written.Contains(i))
                return false;
        }

        return written.Count > 0;
    }

    private void WriteApplied(RegistryTweakSpec spec)
    {
        if (spec.DeleteValueOnEnable)
        {
            _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
            return;
        }

        var kind = RegistryManager.ParseKind(spec.ValueKind);
        var target = spec.ApplyMeansDisable ? spec.DisabledValue : spec.EnabledValue;
        _registry.SetValue(spec.Hive, spec.Path, spec.Name, RegistryManager.ParseValue(target, kind), kind);
    }

    private void RollbackWritten(IReadOnlyList<int> written)
    {
        for (var n = written.Count - 1; n >= 0; n--)
        {
            var i = written[n];
            var spec = _specs[i];
            var prior = i < _previousValues.Count ? _previousValues[i] : null;
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
        _legacyUnknown = false;
        if (string.IsNullOrEmpty(oldValue))
        {
            _seeded = false;
            return;
        }

        _seeded = true;
        if (oldValue is "not-applied" or "applied")
        {
            // Older journals stored a word instead of each prior value.
            _legacyUnknown = true;
            return;
        }

        foreach (var part in oldValue.Split('\u001f'))
            _previousValues.Add(part == "\u0000" ? null : part);
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (!_seeded)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        if (_legacyUnknown)
            return LegacyRevert();

        if (_previousValues.Count == 0)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        var restored = 0;
        try
        {
            for (var i = 0; i < _specs.Count; i++)
            {
                var previous = i < _previousValues.Count ? _previousValues[i] : null;
                RestoreOne(_specs[i], previous);
                restored++;
            }
        }
        catch (Exception ex)
        {
            ReapplyEnabled(restored);
            return Task.FromResult(ChangeResult.Fail(ex.Message));
        }

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Registry values reverted"));
    }

    private Task<ChangeResult> LegacyRevert()
    {
        try
        {
            foreach (var spec in _specs)
            {
                if (spec.DeleteValueOnDisable || spec.DeleteValueOnEnable)
                    _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
                else
                {
                    var kind = RegistryManager.ParseKind(spec.ValueKind);
                    var fallback = spec.ApplyMeansDisable ? spec.EnabledValue : spec.DisabledValue;
                    _registry.SetValue(spec.Hive, spec.Path, spec.Name, RegistryManager.ParseValue(fallback, kind), kind);
                }
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ChangeResult.Fail(ex.Message));
        }

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Registry values reverted"));
    }

    /// <summary>A null prior means the value did not exist. Exact revert deletes it.</summary>
    private void RestoreOne(RegistryTweakSpec spec, string? previous)
    {
        if (previous is not null)
        {
            var kind = RegistryManager.ParseKind(spec.ValueKind);
            _registry.SetValue(spec.Hive, spec.Path, spec.Name, RegistryManager.ParseValue(previous, kind), kind);
            return;
        }

        _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
    }

    private void ReapplyEnabled(int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            try
            {
                WriteApplied(_specs[i]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Registry revert rollback failed for {_specs[i].Path}: {ex.Message}");
            }
        }
    }

    public ActionPreview BuildPreview()
    {
        var rows = _registry is RegistryManager manager
            ? _specs.Select(s => ActionPreviewBuilder.RegistryRow(manager, s)).ToList()
            : new List<PreviewDiffRow>();
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

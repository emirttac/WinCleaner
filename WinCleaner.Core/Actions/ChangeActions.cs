using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public static class RiskParser
{
    public static RiskLevel Parse(string? risk) =>
        risk?.ToLowerInvariant() switch
        {
            "blocked" => RiskLevel.Blocked,
            "dangerous" => RiskLevel.Dangerous,
            "caution" => RiskLevel.Caution,
            _ => RiskLevel.Safe
        };
}

public sealed class ServiceChangeAction : IChangeAction
{
    private readonly ServiceManager _services;
    private readonly string _serviceName;
    private readonly ServiceStartModeTarget _desired;
    private ServiceStartMode? _previousStart;
    private bool _wasRunning;

    public ServiceChangeAction(
        string id,
        string displayName,
        string description,
        string category,
        RiskLevel risk,
        string serviceName,
        ServiceStartModeTarget desired,
        ServiceManager services)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        Risk = risk;
        _serviceName = serviceName;
        _desired = desired;
        _services = services;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var info = _services.GetService(_serviceName);
        if (info is null) return Task.FromResult<string?>("NotInstalled");
        return Task.FromResult<string?>($"{info.StartType}/{info.Status}");
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var info = _services.GetService(_serviceName);
        if (info is null)
            return Task.FromResult(ChangeResult.Fail($"Service {_serviceName} not found."));

        _previousStart = info.StartType;
        _wasRunning = info.Status == ServiceControllerStatus.Running;
        var oldValue = $"{info.StartType}/{info.Status}";

        if (_desired == ServiceStartModeTarget.Disabled && _wasRunning)
            _services.Stop(_serviceName);

        _services.SetStartType(_serviceName, _desired);

        if (_desired == ServiceStartModeTarget.Automatic)
        {
            try { _services.Start(_serviceName); } catch { /* may already be running */ }
        }

        var newInfo = _services.GetService(_serviceName);
        return Task.FromResult(ChangeResult.Ok(oldValue, $"{_desired}/{newInfo?.Status}", "Service updated"));
    }

    public bool IsApplied()
    {
        var info = _services.GetService(_serviceName);
        if (info is null) return false;
        return _desired switch
        {
            ServiceStartModeTarget.Disabled => info.StartType == ServiceStartMode.Disabled,
            ServiceStartModeTarget.Automatic => info.StartType == ServiceStartMode.Automatic,
            _ => info.StartType == ServiceStartMode.Manual
        };
    }

    public void SeedPreviousState(string? oldValue)
    {
        if (string.IsNullOrWhiteSpace(oldValue))
            return;

        // Format from ApplyAsync: "{StartType}/{Status}"
        var parts = oldValue.Split('/', 2);
        if (parts.Length == 0)
            return;

        _previousStart = ParseStartMode(parts[0]);

        if (parts.Length > 1)
        {
            _wasRunning = parts[1].Contains("Running", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ServiceStartMode? ParseStartMode(string raw)
    {
        if (Enum.TryParse<ServiceStartMode>(raw, ignoreCase: true, out var start))
            return start;
        if (raw.Contains("Automatic", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Auto", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Automatic;
        if (raw.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Disabled;
        if (raw.Contains("Manual", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Demand", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Manual;
        return null;
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_previousStart is null)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        var target = _previousStart.Value switch
        {
            ServiceStartMode.Automatic => ServiceStartModeTarget.Automatic,
            ServiceStartMode.Disabled => ServiceStartModeTarget.Disabled,
            _ => ServiceStartModeTarget.Manual
        };

        _services.SetStartType(_serviceName, target);
        if (_wasRunning && target != ServiceStartModeTarget.Disabled)
        {
            try { _services.Start(_serviceName); } catch { }
        }
        else if (!_wasRunning)
        {
            try { _services.Stop(_serviceName); } catch { }
        }

        return Task.FromResult(ChangeResult.Ok(null, _previousStart.ToString(), "Service reverted"));
    }
}

public sealed class RegistryChangeAction : IChangeAction
{
    private readonly RegistryManager _registry;
    private readonly RegistryTweakSpec _spec;
    private string? _previousValue;
    private readonly bool _isInterfaceWildcard;

    public RegistryChangeAction(
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
        _registry = registry;
        _spec = entry.Registry ?? throw new ArgumentException("Missing registry spec");
        _isInterfaceWildcard = _spec.ApplyToAllSubkeys
            || (_spec.Path.EndsWith("\\Interfaces", StringComparison.OrdinalIgnoreCase)
                && (_spec.Name is "TcpAckFrequency" or "TCPNoDelay" or "NetbiosOptions"));
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Category { get; }
    public RiskLevel Risk { get; }
    public bool CanRevert => true;
    public bool RequiresReboot { get; }
    public bool RequiresAntiCheatConfirm { get; }

    private string KeyPathToDelete =>
        string.IsNullOrWhiteSpace(_spec.DeleteKeyPath) ? _spec.Path : _spec.DeleteKeyPath!;

    private bool IsClassicContextMenuTweak =>
        _spec.DeleteKeyOnDisable
        && _spec.Path.Contains("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", StringComparison.OrdinalIgnoreCase);

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        if (IsClassicContextMenuTweak)
            return Task.FromResult<string?>(_registry.IsClassicContextMenuEnabled() ? "key-present" : "key-absent");
        if (_spec.DeleteKeyOnDisable)
            return Task.FromResult<string?>(_registry.KeyExists(_spec.Hive, _spec.Path) ? "key-present" : "key-absent");
        if (_isInterfaceWildcard)
            return Task.FromResult<string?>("per-interface");
        return Task.FromResult(_registry.GetValueAsString(_spec.Hive, _spec.Path, _spec.Name));
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (IsClassicContextMenuTweak)
        {
            var old = _registry.IsClassicContextMenuEnabled() ? "key-present" : "key-absent";
            _registry.EnableClassicContextMenu();
            MaybeRestartExplorer();
            return Task.FromResult(ChangeResult.Ok(old, "key-present", "Classic context menu enabled"));
        }

        if (_spec.DeleteKeyOnDisable)
        {
            var old = _registry.KeyExists(_spec.Hive, _spec.Path) ? "key-present" : "key-absent";
            var kind = RegistryManager.ParseKind(_spec.ValueKind);
            var newVal = RegistryManager.ParseValue(_spec.EnabledValue, kind);
            _registry.SetValue(_spec.Hive, _spec.Path, _spec.Name, newVal, kind);
            MaybeRestartExplorer();
            return Task.FromResult(ChangeResult.Ok(old, "key-present", "Registry key applied"));
        }

        var previous = _isInterfaceWildcard
            ? "per-interface"
            : _registry.GetValueAsString(_spec.Hive, _spec.Path, _spec.Name);
        _previousValue = previous;

        if (_spec.DeleteValueOnEnable)
        {
            if (_isInterfaceWildcard)
                _registry.DeleteValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name);
            else
                _registry.DeleteValue(_spec.Hive, _spec.Path, _spec.Name);

            MaybeRestartExplorer();
            return Task.FromResult(ChangeResult.Ok(previous, "(deleted)", "Registry value deleted"));
        }

        var valueKind = RegistryManager.ParseKind(_spec.ValueKind);
        var enabled = RegistryManager.ParseValue(_spec.EnabledValue, valueKind);

        if (_isInterfaceWildcard)
        {
            var written = _registry.SetValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name, enabled, valueKind);
            if (written == 0)
                return Task.FromResult(ChangeResult.Fail("No interface subkeys found to update."));
        }
        else
            _registry.SetValue(_spec.Hive, _spec.Path, _spec.Name, enabled, valueKind);

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok(previous, _spec.EnabledValue, "Registry value set"));
    }

    public void SeedPreviousState(string? oldValue) => _previousValue = oldValue;

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_spec.DeleteKeyOnDisable)
        {
            _registry.DeleteKeyTree(_spec.Hive, KeyPathToDelete);
            MaybeRestartExplorer();
            return Task.FromResult(ChangeResult.Ok("key-present", "key-absent", "Registry key removed"));
        }

        if (_spec.DeleteValueOnDisable || _spec.DeleteValueOnEnable)
        {
            if (_previousValue is not null && _previousValue != "per-interface")
            {
                var kindRestore = RegistryManager.ParseKind(_spec.ValueKind);
                var val = RegistryManager.ParseValue(_previousValue, kindRestore);
                if (_isInterfaceWildcard)
                    _registry.SetValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name, val, kindRestore);
                else
                    _registry.SetValue(_spec.Hive, _spec.Path, _spec.Name, val, kindRestore);
            }
            else if (_isInterfaceWildcard && _spec.DeleteValueOnDisable)
            {
                _registry.DeleteValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name);
            }
            else if (_previousValue is null || _previousValue == "per-interface")
            {
                // Applied by deleting; prior state was already absent (or unknown per-interface).
                if (!_isInterfaceWildcard)
                    _registry.DeleteValue(_spec.Hive, _spec.Path, _spec.Name);
            }

            MaybeRestartExplorer();
            return Task.FromResult(ChangeResult.Ok(_spec.EnabledValue, _previousValue ?? "(deleted)", "Registry value restored"));
        }

        if (_previousValue is null)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        var kind = RegistryManager.ParseKind(_spec.ValueKind);
        if (_previousValue != "per-interface")
        {
            var val = RegistryManager.ParseValue(_previousValue, kind);
            if (_isInterfaceWildcard)
                _registry.SetValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name, val, kind);
            else
                _registry.SetValue(_spec.Hive, _spec.Path, _spec.Name, val, kind);
        }
        else
        {
            // Per-interface wildcard: restore using catalog disabled value across subkeys
            // only when the journal explicitly recorded the wildcard marker.
            var fallback = RegistryManager.ParseValue(_spec.DisabledValue, kind);
            _registry.SetValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name, fallback, kind);
        }

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok(_spec.EnabledValue, _previousValue, "Registry reverted"));
    }

    public bool IsApplied()
    {
        if (IsClassicContextMenuTweak)
            return _registry.IsClassicContextMenuEnabled();

        if (_spec.DeleteKeyOnDisable)
            return _registry.KeyExists(_spec.Hive, _spec.Path);

        if (_spec.DeleteValueOnEnable)
        {
            if (_isInterfaceWildcard)
                return _registry.AreAllSubkeysMissing(_spec.Hive, _spec.Path, _spec.Name);
            return _registry.GetValueAsString(_spec.Hive, _spec.Path, _spec.Name) is null;
        }

        if (_isInterfaceWildcard)
            return _registry.AreAllSubkeysEqual(_spec.Hive, _spec.Path, _spec.Name, _spec.EnabledValue);

        var current = _registry.GetValueAsString(_spec.Hive, _spec.Path, _spec.Name);

        if (_spec.EnabledValue.Length == 0)
            return current is not null && current.Length == 0;

        return RegistryManager.ValuesEqual(current, _spec.EnabledValue, _spec.ValueKind);
    }

    private void MaybeRestartExplorer()
    {
        if (!_spec.RestartExplorer && !IsClassicContextMenuTweak)
            return;

        // CLSID / key-tree changes need a hard shell restart. Soft UI flags only need a broadcast
        // (avoids explorer folder windows + shutdown dialogs from aggressive restarts).
        if (IsClassicContextMenuTweak || _spec.DeleteKeyOnDisable)
            ExplorerRestarter.Restart();
        else
            ExplorerRestarter.Refresh();
    }
}

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

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(IsApplied() ? "applied" : "not-applied");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _previousValues.Clear();
        foreach (var spec in _specs)
        {
            _previousValues.Add(_registry.GetValueAsString(spec.Hive, spec.Path, spec.Name));
            if (spec.DeleteValueOnEnable)
            {
                _registry.DeleteValue(spec.Hive, spec.Path, spec.Name);
                continue;
            }

            var kind = RegistryManager.ParseKind(spec.ValueKind);
            var enabled = RegistryManager.ParseValue(spec.EnabledValue, kind);
            _registry.SetValue(spec.Hive, spec.Path, spec.Name, enabled, kind);
        }

        MaybeRestartExplorer();
        // Encode per-value priors so SeedPreviousState can restore after restart.
        var packed = string.Join("\u001f", _previousValues.Select(v => v ?? "\u0000"));
        return Task.FromResult(ChangeResult.Ok(packed, "applied", $"Applied {_specs.Count} registry values"));
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

public sealed class CommandChangeAction : IChangeAction
{
    private readonly CommandSpec _spec;
    private readonly Func<string, string> _localize;
    private readonly bool _requiresReboot;

    public CommandChangeAction(
        string id,
        string displayName,
        string description,
        string category,
        RiskLevel risk,
        CommandSpec spec,
        Func<string, string>? localize = null,
        bool requiresReboot = false,
        bool requiresAntiCheatConfirm = false)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        Risk = risk;
        _spec = spec;
        _localize = localize ?? (k => k);
        _requiresReboot = requiresReboot;
        RequiresAntiCheatConfirm = requiresAntiCheatConfirm;
        CanRevert = !string.IsNullOrWhiteSpace(spec.RevertArguments);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert { get; }
    public bool RequiresReboot => _requiresReboot;
    public bool RequiresAntiCheatConfirm { get; }
    public bool IsOneShot => _spec.OneShot;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(IsApplied() ? "applied" : "not-applied");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_spec.RequiresAdmin && !PrivilegeHelper.IsAdministrator())
                return Task.FromResult(ChangeResult.Fail(_localize("common.adminRequired")));

            if (!_spec.OneShot && HasDetectSpec() && IsApplied())
                return Task.FromResult(ChangeResult.Ok("applied", "applied", "Already applied"));

            var run = ElevatedCommandRunner.Run(_spec.FileName, _spec.Arguments, _spec.TimeoutMs ?? 30_000);
            if (!run.Success)
                return Task.FromResult(ChangeResult.Fail(FormatFailure(run)));

            return Task.FromResult(ChangeResult.Ok("not-applied", "applied",
                _spec.OneShot ? _localize("msg.oneShotOk") : "Command executed"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ChangeResult.Fail(FormatException(ex)));
        }
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_spec.RevertArguments))
                return Task.FromResult(ChangeResult.Fail("Not revertible"));

            if (_spec.RequiresAdmin && !PrivilegeHelper.IsAdministrator())
                return Task.FromResult(ChangeResult.Fail(_localize("common.adminRequired")));

            if (HasDetectSpec() && !IsApplied())
                return Task.FromResult(ChangeResult.Ok("not-applied", "not-applied", "Already reverted"));

            var run = ElevatedCommandRunner.Run(_spec.FileName, _spec.RevertArguments, _spec.TimeoutMs ?? 30_000);
            if (!run.Success)
                return Task.FromResult(ChangeResult.Fail(FormatFailure(run)));

            return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Command reverted"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ChangeResult.Fail(FormatException(ex)));
        }
    }

    public bool IsApplied()
    {
        if (_spec.OneShot)
            return false;

        if (!HasDetectSpec())
            return false;

        try
        {
            if (!string.IsNullOrWhiteSpace(_spec.DetectFileName))
                return IsAppliedFromProbeCommand();

            var key = _spec.DetectKey!;
            var mode = (_spec.DetectMode ?? "equals").Trim().ToLowerInvariant();
            var current = ElevatedCommandRunner.ReadBcdValue(key);

            return mode switch
            {
                "absent" => current is null,
                "present" => current is not null,
                _ => current is not null && ValuesMatch(current, _spec.DetectValue)
            };
        }
        catch
        {
            return false;
        }
    }

    private bool IsAppliedFromProbeCommand()
    {
        var run = ElevatedCommandRunner.Run(_spec.DetectFileName!, _spec.DetectArguments ?? "", timeoutMs: 45_000);
        var output = (run.StdOut ?? "") + Environment.NewLine + (run.StdErr ?? "");
        var mode = (_spec.DetectMode ?? "contains").Trim().ToLowerInvariant();

        return mode switch
        {
            "lacks" => !CommandStateProbe.OutputContains(output, _spec.DetectValue),
            "acindexequals" => AcIndexEquals(output, _spec.DetectValue),
            "equals" => ValuesMatch(output.Trim(), _spec.DetectValue),
            _ => CommandStateProbe.OutputContains(output, _spec.DetectValue)
        };
    }

    private static bool AcIndexEquals(string output, string? expected)
    {
        var index = CommandStateProbe.ParsePowerCfgAcSettingIndex(output);
        if (!index.HasValue)
            return false;
        if (!int.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var want))
            return false;
        return index.Value == want;
    }

    private bool HasDetectSpec() =>
        !string.IsNullOrWhiteSpace(_spec.DetectKey) || !string.IsNullOrWhiteSpace(_spec.DetectFileName);

    private static bool ValuesMatch(string actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return !string.IsNullOrWhiteSpace(actual);

        if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        // bcdedit Yes/No across locales / spellings
        var a = actual.Trim();
        var e = expected.Trim();
        if (IsTruthy(e)) return IsTruthy(a);
        if (IsFalsy(e)) return IsFalsy(a);
        return false;
    }

    private static bool IsTruthy(string v) =>
        v.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || v.Equals("true", StringComparison.OrdinalIgnoreCase)
        || v.Equals("1", StringComparison.OrdinalIgnoreCase)
        || v.Equals("on", StringComparison.OrdinalIgnoreCase)
        || v.Equals("evet", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalsy(string v) =>
        v.Equals("no", StringComparison.OrdinalIgnoreCase)
        || v.Equals("false", StringComparison.OrdinalIgnoreCase)
        || v.Equals("0", StringComparison.OrdinalIgnoreCase)
        || v.Equals("off", StringComparison.OrdinalIgnoreCase)
        || v.Equals("hayır", StringComparison.OrdinalIgnoreCase)
        || v.Equals("hayir", StringComparison.OrdinalIgnoreCase);

    private string FormatFailure(ProcessCommandResult run)
    {
        var msg = run.ErrorMessage ?? "Command failed";
        if (_spec.RequiresAdmin && !PrivilegeHelper.IsAdministrator())
            return _localize("common.adminRequired") + " " + msg;
        if (msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("erişim engellendi", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
            return _localize("common.adminRequired") + " " + msg;
        return msg;
    }

    private string FormatException(Exception ex)
    {
        if (!PrivilegeHelper.IsAdministrator() || _spec.RequiresAdmin)
            return _localize("common.adminRequired") + " " + ex.Message;
        return ex.Message;
    }
}

public sealed class AppxRemoveAction : IChangeAction
{
    private readonly AppxManager _appx;
    private readonly IReadOnlyList<string> _packageNames;

    public AppxRemoveAction(string id, string displayName, string description, RiskLevel risk, string packageName, AppxManager appx)
        : this(id, displayName, description, risk, new[] { packageName }, appx)
    {
    }

    public AppxRemoveAction(
        string id,
        string displayName,
        string description,
        RiskLevel risk,
        IReadOnlyList<string> packageNames,
        AppxManager appx)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Risk = risk;
        Category = "Bloatware";
        _packageNames = packageNames;
        _appx = appx;
        CanRevert = false;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert { get; }

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("installed?");

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.RemovePackageAsync(_packageNames, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorDetail);
        return ChangeResult.Ok("installed", "removed", "Package removed");
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail("AppX removal cannot be automatically reverted. Reinstall from Microsoft Store."));
}

public sealed class OneDriveUninstallAction : IChangeAction
{
    private readonly AppxManager _appx;
    private readonly Func<string, string> _localize;

    public OneDriveUninstallAction(AppxManager appx, Func<string, string>? localize = null)
    {
        _appx = appx;
        _localize = localize ?? (k => k);
    }

    public string Id => "onedrive-uninstall";
    public string DisplayName => _localize("blo.onedrive");
    public string Description => _localize("blo.onedriveConfirm");
    public RiskLevel Risk => RiskLevel.Dangerous;
    public string Category => "Bloatware";
    public bool CanRevert => false;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("installed?");

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.UninstallOneDriveAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorDetail);
        return ChangeResult.Ok("installed", "uninstalled", _localize("blo.onedriveOk"));
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail("OneDrive uninstall cannot be auto-reverted."));
}

public sealed class TaskDisableAction : IChangeAction
{
    private readonly TaskSchedulerManager _tasks;
    private readonly string _taskPath;
    private bool _wasEnabled = true;

    public TaskDisableAction(string id, string displayName, string description, string taskPath, TaskSchedulerManager tasks)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = "Telemetry";
        Risk = RiskLevel.Safe;
        _taskPath = taskPath;
        _tasks = tasks;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_tasks.IsTaskEnabled(_taskPath) ? "Enabled" : "Disabled");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _wasEnabled = _tasks.IsTaskEnabled(_taskPath);
        if (!_tasks.TaskExists(_taskPath))
            return Task.FromResult(ChangeResult.Ok(_wasEnabled ? "Enabled" : "Disabled", "Disabled", "Task not present"));
        _tasks.SetTaskEnabled(_taskPath, enabled: false);
        return Task.FromResult(ChangeResult.Ok(_wasEnabled ? "Enabled" : "Disabled", "Disabled"));
    }

    public bool IsApplied()
    {
        if (!_tasks.TaskExists(_taskPath))
            return true;
        return !_tasks.IsTaskEnabled(_taskPath);
    }

    public void SeedPreviousState(string? oldValue)
    {
        if (string.Equals(oldValue, "Enabled", StringComparison.OrdinalIgnoreCase))
            _wasEnabled = true;
        else if (string.Equals(oldValue, "Disabled", StringComparison.OrdinalIgnoreCase))
            _wasEnabled = false;
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        _tasks.SetTaskEnabled(_taskPath, _wasEnabled);
        return Task.FromResult(ChangeResult.Ok("Disabled", _wasEnabled ? "Enabled" : "Disabled"));
    }
}

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
}

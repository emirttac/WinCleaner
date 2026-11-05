using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

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
        Constraints = ActionConstraints.From(entry.Skus, entry.Archs, entry.RequiresFeature);
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
    public ActionConstraints Constraints { get; }
    public bool WritesMachine =>
        _spec.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ||
        _spec.Hive.Equals("HKCR", StringComparison.OrdinalIgnoreCase);

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
        var writtenValue = _spec.ApplyMeansDisable ? _spec.DisabledValue : _spec.EnabledValue;
        var enabled = RegistryManager.ParseValue(writtenValue, valueKind);

        if (_isInterfaceWildcard)
        {
            var written = _registry.SetValueOnAllSubkeys(_spec.Hive, _spec.Path, _spec.Name, enabled, valueKind);
            if (written == 0)
                return Task.FromResult(ChangeResult.Fail("No interface subkeys found to update."));
        }
        else
            _registry.SetValue(_spec.Hive, _spec.Path, _spec.Name, enabled, valueKind);

        MaybeRestartExplorer();
        return Task.FromResult(ChangeResult.Ok(previous, writtenValue, "Registry value set"));
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

    public ActionPreview BuildPreview()
    {
        var row = ActionPreviewBuilder.RegistryRow(_registry, _spec);
        return ActionPreviewDefaults.FromAction(this, row.Current, row.Proposed, row.Kind, [row]);
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

        var expected = _spec.ApplyMeansDisable ? _spec.DisabledValue : _spec.EnabledValue;
        if (expected.Length == 0)
            return current is not null && current.Length == 0;

        return RegistryManager.ValuesEqual(current, expected, _spec.ValueKind);
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

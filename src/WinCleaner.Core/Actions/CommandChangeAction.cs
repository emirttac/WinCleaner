using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class CommandChangeAction : IChangeAction
{
    private readonly CommandSpec _spec;
    private readonly Func<string, string> _localize;
    private readonly bool _requiresReboot;
    private string? _captured;
    private bool _irreversibleAccepted;
    private bool _canRevert;
    private bool _leaveEnabledFeature;

    public CommandChangeAction(
        string id,
        string displayName,
        string description,
        string category,
        RiskLevel risk,
        CommandSpec spec,
        Func<string, string>? localize = null,
        bool requiresReboot = false,
        bool requiresAntiCheatConfirm = false,
        ActionConstraints? constraints = null)
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
        Constraints = constraints ?? ActionConstraints.None;
        WritesMachine = spec.RequiresAdmin;
        _canRevert = !string.IsNullOrWhiteSpace(spec.RevertArguments) || id is "tweak-core-parking" or "tweak-disable-ipv6" or "tweak-explorer-folder-discovery";
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => _canRevert;
    public bool RequiresReboot => _requiresReboot;
    public bool RequiresAntiCheatConfirm { get; }
    public ActionConstraints Constraints { get; }
    public bool WritesMachine { get; }
    public bool IsOneShot => _spec.OneShot;

    public ActionPreview BuildPreview()
    {
        var current = IsApplied() ? "applied" : "not-applied";
        var proposed = $"{_spec.FileName} {_spec.Arguments}".Trim();
        var kind = CanRevert ? PreviewDiffKind.Script : PreviewDiffKind.Irreversible;
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            kind,
            [
                new PreviewDiffRow
                {
                    Target = $"{_spec.FileName}",
                    Current = current,
                    Proposed = proposed,
                    Kind = kind,
                    Unchanged = !IsOneShot && IsApplied()
                }
            ]);
    }

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(IsApplied() ? "applied" : "not-applied");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_spec.RequiresAdmin && !PrivilegeHelper.IsAdministrator())
                return Task.FromResult(ChangeResult.Fail(_localize("common.adminRequired")));

            if (Id == "tweak-core-parking")
                return ApplyCoreParking();

            if (!_spec.OneShot && HasDetectSpec() && IsApplied())
            {
                if (!string.IsNullOrWhiteSpace(Constraints.RequiresFeature))
                    _leaveEnabledFeature = true;
                return Task.FromResult(ChangeResult.Ok("applied", "applied", "exec.alreadyApplied"));
            }

            CaptureKnownState();
            if (CommandRevertBuilder.NeedsIrreversibleConfirm(Id, _captured, _irreversibleAccepted))
                return Task.FromResult(ChangeResult.FailCode("gate.irreversibleConfirm"));

            var run = ElevatedCommandRunner.Run(_spec.FileName, _spec.Arguments, _spec.TimeoutMs ?? 30_000);
            if (!run.Success)
                return Task.FromResult(ChangeResult.Fail(FormatFailure(run)));

            return Task.FromResult(ChangeResult.Ok(_captured ?? "not-applied", "applied",
                _spec.OneShot ? _localize("msg.oneShotOk") : "Command executed"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ChangeResult.Fail(FormatException(ex)));
        }
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_leaveEnabledFeature)
            return Task.FromResult(ChangeResult.Ok("applied", "applied", "feature.leftEnabled"));

        if (Id == "tweak-core-parking")
            return RevertCoreParking();

        try
        {
            if (CommandRevertBuilder.TryBuild(Id, _captured, out var fileName, out var arguments))
            {
                var exact = ElevatedCommandRunner.Run(fileName, arguments, _spec.TimeoutMs ?? 30_000);
                if (!exact.Success)
                    return Task.FromResult(ChangeResult.Fail(FormatFailure(exact)));
                return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Command reverted"));
            }

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

    public void SeedPreviousState(string? oldValue)
    {
        _captured = oldValue;
        if (!string.IsNullOrWhiteSpace(Constraints.RequiresFeature)
            && string.Equals(oldValue, "applied", StringComparison.OrdinalIgnoreCase))
            _leaveEnabledFeature = true;
    }

    public void AcceptIrreversible() => _irreversibleAccepted = true;

    private Task<ChangeResult> ApplyCoreParking()
    {
        foreach (var alias in new[] { "CPMINCORES", "CPMINCORES1" })
            ElevatedCommandRunner.Run("powercfg", $"-attributes SUB_PROCESSOR {alias} -ATTRIB_HIDE", 15000);

        CaptureKnownState();
        if (string.IsNullOrEmpty(_captured))
            return Task.FromResult(ChangeResult.Fail(_localize("msg.coreParkingUnavailable")));

        foreach (var args in CoreParking.ApplyValueArguments)
        {
            var run = ElevatedCommandRunner.Run("powercfg", args, 15000);
            if (run.Success)
                continue;
            var detail = (run.StdErr + "\n" + run.StdOut + "\n" + run.ErrorMessage).Trim();
            if (CoreParking.IsOptionalAlias(args) && CoreParking.IsSettingMissing(detail))
                continue;
            return Task.FromResult(ChangeResult.Fail(FormatFailure(run)));
        }

        var active = ElevatedCommandRunner.Run("powercfg", "-setactive scheme_current", 15000);
        if (!active.Success)
            return Task.FromResult(ChangeResult.Fail(FormatFailure(active)));

        var verify = ElevatedCommandRunner.Run("powercfg", "/query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES", 15000);
        var ac = CommandStateProbe.ParsePowerCfgAcSettingIndex(verify.StdOut);
        if (ac != 100)
            return Task.FromResult(ChangeResult.Fail(_localize("msg.coreParkingUnavailable")));

        return Task.FromResult(ChangeResult.Ok(_captured, "applied", "Command executed"));
    }

    private Task<ChangeResult> RevertCoreParking()
    {
        if (string.IsNullOrWhiteSpace(_captured) || !int.TryParse(_captured, NumberStyles.Integer, CultureInfo.InvariantCulture, out var previous))
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        foreach (var args in new[]
        {
            $"-setacvalueindex scheme_current SUB_PROCESSOR CPMINCORES {previous}",
            $"-setdcvalueindex scheme_current SUB_PROCESSOR CPMINCORES {previous}",
            $"-setacvalueindex scheme_current SUB_PROCESSOR CPMINCORES1 {previous}",
            $"-setdcvalueindex scheme_current SUB_PROCESSOR CPMINCORES1 {previous}"
        })
        {
            var run = ElevatedCommandRunner.Run("powercfg", args, 15000);
            if (run.Success)
                continue;
            var detail = (run.StdErr + "\n" + run.StdOut + "\n" + run.ErrorMessage).Trim();
            if (args.Contains("CPMINCORES1", StringComparison.OrdinalIgnoreCase) && CoreParking.IsSettingMissing(detail))
                continue;
            return Task.FromResult(ChangeResult.Fail(FormatFailure(run)));
        }

        var active = ElevatedCommandRunner.Run("powercfg", "-setactive scheme_current", 15000);
        if (!active.Success)
            return Task.FromResult(ChangeResult.Fail(FormatFailure(active)));

        return Task.FromResult(ChangeResult.Ok("applied", "not-applied", "Command reverted"));
    }

    private void CaptureKnownState()
    {
        if (Id == "tweak-core-parking")
        {
            var query = ElevatedCommandRunner.Run("powercfg", "/query SCHEME_CURRENT SUB_PROCESSOR CPMINCORES", 15000);
            var ac = CommandStateProbe.ParsePowerCfgAcSettingIndex(query.StdOut);
            _captured = ac?.ToString() ?? "";
            return;
        }

        if (Id == "tweak-disable-ipv6")
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
                "DisabledComponents",
                null);
            _captured = value is null ? "absent" : Convert.ToInt32(value).ToString();
            return;
        }

        if (Id == "tweak-explorer-folder-discovery")
        {
            var dir = Path.Combine(Path.GetTempPath(), "WinCleaner", "captures");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "bags-" + Guid.NewGuid().ToString("N") + ".reg");
            var export = ElevatedCommandRunner.Run("reg.exe", "export \"HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\Bags\" \"" + file + "\" /y", 20000);
            _captured = export.Success && File.Exists(file) ? "reg:" + file : "export-failed";
            if (_captured == "export-failed")
                _canRevert = false;
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

        return CommandStateProbe.TokensEqual(actual, expected);
    }

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

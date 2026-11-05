using WinCleaner.Core.Models;
using WinCleaner.Core.Services;

namespace WinCleaner.Core.Actions;

public interface IDnsPresetStore
{
    DnsPresetKind DetectCurrentPreset();
    DnsApplyResult ApplyPreset(DnsPresetKind kind);
}

public sealed class DnsPresetChangeAction : IChangeAction
{
    public const string ActionId = "dns-preset";

    private readonly IDnsPresetStore _dns;
    private readonly DnsPresetKind _target;
    private readonly Func<string, string> _localize;
    private DnsPresetKind _previous = DnsPresetKind.Dhcp;

    public DnsPresetChangeAction(
        IDnsPresetStore dns,
        DnsPresetKind target,
        Func<string, string>? localize = null)
    {
        _dns = dns;
        _target = target;
        _localize = localize ?? (k => k);
    }

    public string Id => ActionId;
    public string DisplayName => _localize("tm.dns.name");
    public string Description => _localize("tm.dns.desc");
    public RiskLevel Risk => RiskLevel.Safe;
    public string Category => "Network";
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_dns.DetectCurrentPreset().ToString());

    public bool IsApplied() => _dns.DetectCurrentPreset() == _target;

    public ActionPreview BuildPreview()
    {
        var current = _dns.DetectCurrentPreset().ToString();
        var proposed = _target.ToString();
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = "dns:adapters",
                    Current = current,
                    Proposed = proposed,
                    Kind = PreviewDiffKind.Set,
                    Unchanged = current == proposed
                }
            ]);
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _previous = _dns.DetectCurrentPreset();
        var result = _dns.ApplyPreset(_target);
        if (!result.Success)
            return Task.FromResult(ChangeResult.Fail(result.Message));
        return Task.FromResult(ChangeResult.Ok(_previous.ToString(), _target.ToString(), result.Message));
    }

    public void SeedPreviousState(string? oldValue)
    {
        if (Enum.TryParse<DnsPresetKind>(oldValue, ignoreCase: true, out var kind))
            _previous = kind;
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        var result = _dns.ApplyPreset(_previous);
        if (!result.Success)
            return Task.FromResult(ChangeResult.Fail(result.Message));
        return Task.FromResult(ChangeResult.Ok(_target.ToString(), _previous.ToString(), result.Message));
    }
}

public sealed class StartupToggleAction : IChangeAction
{
    public const string IdPrefix = "startup:";

    private readonly StartupManager _startup;
    private readonly StartupEntry _entry;
    private readonly bool _desiredEnabled;
    private string? _previousPacked;

    public StartupToggleAction(StartupManager startup, StartupEntry entry, bool desiredEnabled)
    {
        _startup = startup;
        _entry = entry;
        _desiredEnabled = desiredEnabled;
    }

    public string Id => $"{IdPrefix}{_entry.Hive}:{_entry.Name}";
    public string DisplayName => _entry.Name;
    public string Description => _entry.Command;
    public RiskLevel Risk => RiskLevel.Safe;
    public string Category => "Visual";
    public bool CanRevert => true;

    public static bool TryParseId(string actionId, out string hive, out string name)
    {
        hive = "";
        name = "";
        if (!actionId.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = actionId[IdPrefix.Length..];
        var idx = rest.IndexOf(':');
        if (idx <= 0)
            return false;
        hive = rest[..idx];
        name = rest[(idx + 1)..];
        return hive.Length > 0 && name.Length > 0;
    }

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var live = _startup.GetStartupEntries()
            .FirstOrDefault(e => e.Hive == _entry.Hive && e.Name == _entry.Name);
        return Task.FromResult<string?>(live is null ? "disabled" : $"enabled|{live.Command}");
    }

    public bool IsApplied()
    {
        var live = _startup.GetStartupEntries()
            .Any(e => e.Hive == _entry.Hive && e.Name == _entry.Name);
        return _desiredEnabled ? live : !live;
    }

    public ActionPreview BuildPreview()
    {
        var current = GetCurrentStateAsync().GetAwaiter().GetResult() ?? "disabled";
        var proposed = _desiredEnabled ? $"enabled|{_entry.Command}" : "disabled";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = $"{_entry.Hive}\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\\{_entry.Name}",
                    Current = current,
                    Proposed = proposed,
                    Kind = _desiredEnabled ? PreviewDiffKind.Set : PreviewDiffKind.Delete,
                    Unchanged = string.Equals(current, proposed, StringComparison.OrdinalIgnoreCase)
                }
            ]);
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var previous = GetCurrentStateAsync().GetAwaiter().GetResult();
        _previousPacked = previous;
        _startup.SetEnabled(_entry, _desiredEnabled);
        var next = _desiredEnabled ? $"enabled|{_entry.Command}" : "disabled";
        return Task.FromResult(ChangeResult.Ok(previous, next, "Startup entry updated"));
    }

    public void SeedPreviousState(string? oldValue) => _previousPacked = oldValue;

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_previousPacked))
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        if (_previousPacked.StartsWith("enabled|", StringComparison.OrdinalIgnoreCase))
        {
            var command = _previousPacked["enabled|".Length..];
            _startup.SetEnabled(new StartupEntry
            {
                Name = _entry.Name,
                Command = command,
                Hive = _entry.Hive,
                IsEnabled = true
            }, enabled: true);
        }
        else
        {
            _startup.SetEnabled(_entry, enabled: false);
        }

        return Task.FromResult(ChangeResult.Ok(_desiredEnabled ? "enabled" : "disabled", _previousPacked, "Startup entry restored"));
    }
}

public sealed class TakeOwnershipChangeAction : IChangeAction
{
    public const string ActionId = "takeown-menu";

    private readonly SystemHealthService _health;
    private readonly string _menuLabel;
    private readonly bool _desiredEnabled;
    private bool _wasEnabled;

    public TakeOwnershipChangeAction(SystemHealthService health, bool desiredEnabled, string menuLabel)
    {
        _health = health;
        _desiredEnabled = desiredEnabled;
        _menuLabel = string.IsNullOrWhiteSpace(menuLabel) ? "Take Ownership" : menuLabel;
    }

    public string Id => ActionId;
    public string DisplayName => _menuLabel;
    public string Description => "Classic Take Ownership context menu";
    public RiskLevel Risk => RiskLevel.Caution;
    public string Category => "Tools";
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_health.IsTakeOwnershipMenuEnabled() ? "on" : "off");

    public bool IsApplied() => _health.IsTakeOwnershipMenuEnabled() == _desiredEnabled;

    public ActionPreview BuildPreview()
    {
        var current = _health.IsTakeOwnershipMenuEnabled() ? "on" : "off";
        var proposed = _desiredEnabled ? "on" : "off";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = @"HKCR\*\shell\WinCleanerTakeOwn",
                    Current = current,
                    Proposed = proposed,
                    Kind = PreviewDiffKind.Set,
                    Unchanged = current == proposed
                }
            ]);
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _wasEnabled = _health.IsTakeOwnershipMenuEnabled();
        _health.SetTakeOwnershipMenu(_desiredEnabled, _menuLabel);
        return Task.FromResult(ChangeResult.Ok(_wasEnabled ? "on" : "off", _desiredEnabled ? "on" : "off", "Take Ownership menu updated"));
    }

    public void SeedPreviousState(string? oldValue) =>
        _wasEnabled = string.Equals(oldValue, "on", StringComparison.OrdinalIgnoreCase);

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        _health.SetTakeOwnershipMenu(_wasEnabled, _menuLabel);
        return Task.FromResult(ChangeResult.Ok(_desiredEnabled ? "on" : "off", _wasEnabled ? "on" : "off", "Take Ownership menu restored"));
    }
}

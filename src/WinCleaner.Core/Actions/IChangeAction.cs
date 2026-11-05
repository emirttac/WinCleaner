using WinCleaner.Core.Models;

namespace WinCleaner.Core.Actions;

public interface IChangeAction
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    RiskLevel Risk { get; }
    string Category { get; }
    bool CanRevert { get; }
    /// <summary>When true, UI should prompt the user to reboot after a successful apply.</summary>
    bool RequiresReboot => false;
    /// <summary>When true, UI must show anti-cheat risk confirmation before apply.</summary>
    bool RequiresAntiCheatConfirm => false;
    /// <summary>One-shot command: toggle must not stick on; show a success message after apply.</summary>
    bool IsOneShot => false;
    /// <summary>AppX packages the catalog marks as required by Windows.</summary>
    bool SystemCritical => false;
    /// <summary>HKLM or HKCR registry write. The executor checks admin before apply.</summary>
    bool WritesMachine => false;
    ActionConstraints Constraints => ActionConstraints.None;
    Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default);
    Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the live system already matches the applied (on) state.</summary>
    bool IsApplied() => false;

    /// <summary>
    /// Seeds prior state from the change journal so a freshly resolved action can revert correctly.
    /// Default is a no-op for actions that do not store prior values.
    /// </summary>
    void SeedPreviousState(string? oldValue) { }

    /// <summary>
    /// Live current → proposed diff. Must not write registry, services, or files.
    /// </summary>
    ActionPreview BuildPreview()
    {
        string? current = null;
        try { current = GetCurrentStateAsync().GetAwaiter().GetResult(); }
        catch { /* preview is best-effort */ }

        var proposed = !CanRevert ? "removed" : IsOneShot ? "run" : "applied";
        return ActionPreviewDefaults.FromAction(this, current, proposed);
    }
}

public sealed class ChangeResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }

    public static ChangeResult Ok(string? oldValue = null, string? newValue = null, string? message = null) =>
        new() { Success = true, OldValue = oldValue, NewValue = newValue, Message = message };

    public static ChangeResult Fail(string message) =>
        new() { Success = false, Message = message };

    public static ChangeResult FailCode(string code, string? message = null) =>
        new() { Success = false, ErrorCode = code, Message = message ?? code };
}

public sealed record ActionConstraints(
    IReadOnlyList<string> Skus,
    IReadOnlyList<string> Archs,
    string? RequiresFeature)
{
    public static ActionConstraints None { get; } = new(Array.Empty<string>(), Array.Empty<string>(), null);

    public static ActionConstraints From(IReadOnlyList<string>? skus, IReadOnlyList<string>? archs, string? feature) =>
        new(skus ?? Array.Empty<string>(), archs ?? Array.Empty<string>(), feature);
}

public static class GateCodes
{
    public const string Blocked = "gate.blocked";
    public const string Dangerous = "gate.dangerous";
    public const string SystemCritical = "gate.systemCritical";
    public const string AntiCheat = "gate.anticheatDenied";
    public const string Admin = "gate.admin";
    public const string Sku = "gate.sku";
    public const string Arch = "gate.arch";
    public const string Os = "gate.os";
    public const string Dependency = "gate.dependency";
    public const string Arm64 = "gate.arm64Denied";
    public const string Partial = "gate.partial";
}

public sealed class ChangeLogEntry
{
    public required string ActionId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public bool CanRevert { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public string Category { get; init; } = "";
}

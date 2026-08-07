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
    Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default);
    Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default);
    Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeds prior state from the change journal so a freshly resolved action can revert correctly.
    /// Default is a no-op for actions that do not store prior values.
    /// </summary>
    void SeedPreviousState(string? oldValue) { }
}

public sealed class ChangeResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }

    public static ChangeResult Ok(string? oldValue = null, string? newValue = null, string? message = null) =>
        new() { Success = true, OldValue = oldValue, NewValue = newValue, Message = message };

    public static ChangeResult Fail(string message) =>
        new() { Success = false, Message = message };
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
    public string Category { get; init; } = "";
}

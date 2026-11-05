using System.Runtime.InteropServices;
using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Models;

namespace WinCleaner.Core.Actions;

public enum GateKind
{
    Allow,
    Deny,
    NeedsConfirmation
}

public sealed record GateDecision(GateKind Kind, string? ErrorCode);

public sealed class ActionGateContext
{
    public required IChangeAction Action { get; init; }
    public bool IsRevert { get; init; }
    public bool AllowDangerous { get; init; }
    public bool IsAdministrator { get; init; }
    public required ISystemInfo System { get; init; }
    public bool DependencyAvailable { get; init; } = true;
    public bool Arm64Confirmed { get; init; }
    public bool AntiCheatConfirmed { get; init; }
}

public interface IActionGate
{
    GateDecision Decide(ActionGateContext context);
}

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(string code, CancellationToken cancellationToken);
}

public interface IFeatureProbe
{
    bool IsAvailable(string featureName);
}

/// <summary>
/// Faz 3 does not call DISM. Unknown availability stays open so a missing probe cannot hide a tweak.
/// </summary>
public sealed class OpenFeatureProbe : IFeatureProbe
{
    public bool IsAvailable(string featureName) => true;
}

public sealed class ActionGateChain : IActionGate
{
    public GateDecision Decide(ActionGateContext context)
    {
        var action = context.Action;
        if (action.Risk == RiskLevel.Blocked)
            return new GateDecision(GateKind.Deny, GateCodes.Blocked);

        if (!context.IsRevert && action.Risk == RiskLevel.Dangerous && !context.AllowDangerous)
            return new GateDecision(GateKind.Deny, GateCodes.Dangerous);

        if (!context.IsRevert && action.SystemCritical)
            return new GateDecision(GateKind.Deny, GateCodes.SystemCritical);

        var sku = context.System.Sku ?? "Unknown";
        if (!CatalogAccess.IsSkuAllowed(action.Constraints.Skus, sku))
            return new GateDecision(GateKind.Deny, GateCodes.Sku);

        if (!CatalogAccess.IsArchAllowed(action.Constraints.Archs, context.System.OsArch))
            return new GateDecision(GateKind.Deny, GateCodes.Arch);

        if (!context.IsRevert && action.WritesMachine && !context.IsAdministrator)
            return new GateDecision(GateKind.Deny, GateCodes.Admin);

        if (!string.IsNullOrWhiteSpace(action.Constraints.RequiresFeature) && !context.DependencyAvailable)
            return new GateDecision(GateKind.Deny, GateCodes.Dependency);

        if (!context.IsRevert && action.RequiresAntiCheatConfirm && !context.AntiCheatConfirmed)
            return new GateDecision(GateKind.NeedsConfirmation, GateCodes.AntiCheat);

        if (!context.Arm64Confirmed && context.System.OsArch == Architecture.Arm64)
            return new GateDecision(GateKind.NeedsConfirmation, GateCodes.Arm64);

        return new GateDecision(GateKind.Allow, null);
    }
}

public static class CatalogAccess
{
    public static bool IsSkuAllowed(IReadOnlyList<string>? skus, string? currentSku)
    {
        if (skus is null || skus.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(currentSku) || currentSku.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return true;
        return skus.Any(s => s.Equals(currentSku, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsArchAllowed(IReadOnlyList<string>? archs, Architecture osArch)
    {
        if (archs is null || archs.Count == 0)
            return true;
        var name = osArch.ToString();
        return archs.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Allows(Data.TweakCatalogEntry entry, string sku, Architecture osArch)
    {
        if (!IsSkuAllowed(entry.Skus, sku))
            return false;
        return IsArchAllowed(entry.Archs, osArch);
    }
}

public static class SkuMap
{
    public static string FromEdition(int? productType, string? installationType, string? editionId)
    {
        if (productType is 2 or 3)
            return "Server";
        if (!string.IsNullOrWhiteSpace(installationType) &&
            installationType.Contains("Server", StringComparison.OrdinalIgnoreCase))
            return "Server";

        var edition = editionId?.Trim() ?? "";
        if (edition.Length == 0)
            return "Unknown";
        if (edition.Equals("Core", StringComparison.OrdinalIgnoreCase) ||
            edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
            return "Home";
        if (edition.Contains("Education", StringComparison.OrdinalIgnoreCase))
            return "Education";
        if (edition.Equals("EnterpriseS", StringComparison.OrdinalIgnoreCase) ||
            edition.Equals("EnterpriseSN", StringComparison.OrdinalIgnoreCase) ||
            edition.Equals("IoTEnterpriseS", StringComparison.OrdinalIgnoreCase))
            return "LTSC";
        if (edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
            return "Enterprise";
        if (edition.StartsWith("Professional", StringComparison.OrdinalIgnoreCase) ||
            edition.Equals("Pro", StringComparison.OrdinalIgnoreCase))
            return "Pro";
        return "Unknown";
    }
}

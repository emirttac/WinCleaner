using WinCleaner.Data;

namespace WinCleaner.Core.Services;

public static class ReversibilityRules
{
    public static readonly string[] Allowed = ["Exact", "Defaulted", "Scripted", "None"];

    public static string Classify(string id, string type, bool oneShot)
    {
        if (oneShot)
            return "None";
        if (id is "tweak-core-parking" or "tweak-disable-ipv6" or "tweak-explorer-folder-discovery")
            return "Exact";
        return type.ToLowerInvariant() switch
        {
            "visualeffects" => "Defaulted",
            "command" => "Scripted",
            _ => "Exact"
        };
    }

    public static IReadOnlyList<string> Validate(Data.TweakCatalogEntry entry)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(entry.Reversibility))
            errors.Add($"{entry.Id}: reversibility is required");
        else if (!Allowed.Contains(entry.Reversibility, StringComparer.Ordinal))
            errors.Add($"{entry.Id}: reversibility '{entry.Reversibility}' is not Exact, Defaulted, Scripted, or None");
        return errors;
    }
}

public static class CommandRevertBuilder
{
    public static bool TryBuild(string id, string? captured, out string fileName, out string arguments)
    {
        fileName = "";
        arguments = "";
        if (string.IsNullOrWhiteSpace(captured) || captured == "export-failed")
            return false;

        if (id == "tweak-core-parking" && int.TryParse(captured, out var ac))
        {
            fileName = "cmd";
            arguments = "/c powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES " + ac
                + " & powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR CPMINCORES " + ac
                + " & powercfg /setactive SCHEME_CURRENT";
            return true;
        }

        if (id == "tweak-disable-ipv6")
        {
            fileName = "powershell";
            arguments = captured == "absent"
                ? "-NoProfile -Command \"Remove-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters' -Name DisabledComponents -ErrorAction SilentlyContinue\""
                : "-NoProfile -Command \"New-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip6\\Parameters' -Name DisabledComponents -Value " + captured + " -PropertyType DWord -Force\"";
            return true;
        }

        if (id == "tweak-explorer-folder-discovery" && captured.StartsWith("reg:", StringComparison.Ordinal))
        {
            fileName = "reg.exe";
            arguments = "import \"" + captured["reg:".Length..] + "\"";
            return true;
        }

        return false;
    }

    public static bool NeedsIrreversibleConfirm(string id, string? captured, bool accepted) =>
        id == "tweak-explorer-folder-discovery" && captured == "export-failed" && !accepted;
}

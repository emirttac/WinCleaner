using System.Globalization;
using System.Text.RegularExpressions;

namespace WinCleaner.Core.Services;

/// <summary>
/// Locale-tolerant parsers for command-tweak detection (powercfg, netsh, schtasks-style output).
/// </summary>
public static class CommandStateProbe
{
    private static readonly Regex HexIndex = new(@"0x([0-9a-fA-F]+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the current AC setting index from <c>powercfg /query ... CPMINCORES</c> output.
    /// Ignores Minimum/Maximum Possible lines so 0x64 (100%) is not a false positive.
    /// </summary>
    public static int? ParsePowerCfgAcSettingIndex(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        int? found = null;
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (ContainsAny(line, "Maximum", "Minimum", "Possible", "Mögliche", "Possible Settings"))
                continue;
            if (!ContainsAcToken(line))
                continue;
            if (!ContainsIndexToken(line))
                continue;

            var match = HexIndex.Match(line);
            if (!match.Success)
                continue;
            if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                found = value;
        }

        return found;
    }

    public static bool OutputContains(string? output, string? needle) =>
        !string.IsNullOrWhiteSpace(output)
        && !string.IsNullOrWhiteSpace(needle)
        && output.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAcToken(string line) =>
        line.Contains(" AC", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AC-", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AC ", StringComparison.OrdinalIgnoreCase)
        || line.Contains("(AC)", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsIndexToken(string line) =>
        line.Contains("Index", StringComparison.OrdinalIgnoreCase)
        || line.Contains("İndeks", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Indeks", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string line, params string[] tokens)
    {
        foreach (var t in tokens)
        {
            if (line.Contains(t, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

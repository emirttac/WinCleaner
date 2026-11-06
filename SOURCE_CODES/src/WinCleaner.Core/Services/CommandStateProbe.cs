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

        var values = new List<int>();
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = HexIndex.Match(raw);
            if (!match.Success)
                continue;
            if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                values.Add(value);
        }

        // powercfg /query order is the same in every language: minimum, maximum, increment, AC index, DC index.
        if (values.Count >= 4)
            return values[3];
        if (values.Count == 1)
            return values[0];
        return null;
    }

    /// <summary>Case-insensitive token compare. Does not treat translated yes/no words as equal.</summary>
    public static bool TokensEqual(string? actual, string? expected)
    {
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected))
            return false;
        return actual.Trim().Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool OutputContains(string? output, string? needle) =>
        !string.IsNullOrWhiteSpace(output)
        && !string.IsNullOrWhiteSpace(needle)
        && output.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Shared checks for the core-parking powercfg sequence.</summary>
public static class CoreParking
{
    public static readonly string[] ApplyValueArguments =
    [
        "-setacvalueindex scheme_current SUB_PROCESSOR CPMINCORES 100",
        "-setdcvalueindex scheme_current SUB_PROCESSOR CPMINCORES 100",
        "-setacvalueindex scheme_current SUB_PROCESSOR CPMINCORES1 100",
        "-setdcvalueindex scheme_current SUB_PROCESSOR CPMINCORES1 100"
    ];

    public static bool IsOptionalAlias(string arguments) =>
        arguments.Contains("CPMINCORES1", StringComparison.OrdinalIgnoreCase);

    public static bool IsSettingMissing(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || text.Contains("invalid parameters", StringComparison.OrdinalIgnoreCase)
               || text.Contains("bulunamad", StringComparison.OrdinalIgnoreCase)
               || text.Contains("geçersiz", StringComparison.OrdinalIgnoreCase)
               || text.Contains("no existe", StringComparison.OrdinalIgnoreCase)
               || text.Contains("introuvable", StringComparison.OrdinalIgnoreCase)
               || text.Contains("nicht gefunden", StringComparison.OrdinalIgnoreCase)
               || text.Contains("не найден", StringComparison.OrdinalIgnoreCase)
               || text.Contains("nu există", StringComparison.OrdinalIgnoreCase)
               || text.Contains("无效", StringComparison.Ordinal);
    }
}

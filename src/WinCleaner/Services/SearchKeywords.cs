namespace WinCleaner.Services;

/// <summary>
/// Keyword search: query is split into tokens; every token must appear
/// somewhere in the searchable fields (AND). Order does not matter.
/// </summary>
public static class SearchKeywords
{
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ',', ';', '|', '/', '\\'];

    public static IReadOnlyList<string> Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();

        return query
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool Matches(string? query, params string?[] fields)
    {
        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return true;

        var haystack = string.Join('\n', fields.Where(f => !string.IsNullOrWhiteSpace(f)));
        if (haystack.Length == 0)
            return false;

        foreach (var token in tokens)
        {
            if (!haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

using System.Text.Json;
using WinCleaner.Data;

namespace WinCleaner.Core.Services;

public sealed record ConfigRequest(
    string ConfigPath,
    bool Apply,
    bool Silent,
    string? LogPath,
    bool WhatIf,
    bool AllowUnknown);

public sealed class ConfigOutcome
{
    public bool Rejected { get; init; }
    public IReadOnlyList<string> AcceptedIds { get; init; } = [];
    public IReadOnlyList<string> UnknownIds { get; init; } = [];
    public string Summary { get; init; } = "";
}

public static class ConfigArgs
{
    public static bool HasConfig(string[] args) =>
        args.Any(a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(string[] args, out ConfigRequest? request, out string? error)
    {
        request = null;
        error = null;
        string? path = null;
        string? log = null;
        var apply = false;
        var silent = false;
        var whatIf = false;
        var allowUnknown = false;
        var sawConfig = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--apply", StringComparison.OrdinalIgnoreCase)) apply = true;
            else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase)) silent = true;
            else if (arg.Equals("--whatif", StringComparison.OrdinalIgnoreCase)) whatIf = true;
            else if (arg.Equals("--allow-unknown", StringComparison.OrdinalIgnoreCase)) allowUnknown = true;
            else if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                sawConfig = true;
                if (i + 1 >= args.Length) { error = "missing --config path"; return true; }
                path = args[++i];
            }
            else if (arg.Equals("--log", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) { error = "missing --log path"; return false; }
                log = args[++i];
            }
        }

        if (!sawConfig)
            return false;
        if (string.IsNullOrWhiteSpace(path))
        {
            error ??= "missing --config path";
            return true;
        }

        request = new ConfigRequest(path, apply, silent, log, whatIf, allowUnknown);
        return true;
    }
}

public static class ConfigSession
{
    public static ConfigOutcome Plan(CustomProfileDocument document, IReadOnlySet<string> knownIds, bool allowUnknown)
    {
        if (document.SchemaVersion is not (0 or 2 or 3))
        {
            return new ConfigOutcome
            {
                Rejected = true,
                Summary = "schema version must be 2 or 3"
            };
        }

        var unknown = document.TweakIds
            .Where(id => !knownIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var accepted = document.TweakIds
            .Where(id => knownIds.Contains(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0 && !allowUnknown)
        {
            return new ConfigOutcome
            {
                Rejected = true,
                UnknownIds = unknown,
                AcceptedIds = accepted,
                Summary = "unknown id: " + string.Join(", ", unknown)
            };
        }

        return new ConfigOutcome
        {
            Rejected = false,
            UnknownIds = unknown,
            AcceptedIds = accepted,
            Summary = (allowUnknown && unknown.Count > 0 ? "skipped unknown: " + string.Join(", ", unknown) + ". " : "")
                      + "accepted: " + accepted.Count
        };
    }

    public static async Task<ConfigOutcome> RunAsync(
        ConfigRequest request,
        IReadOnlySet<string> knownIds,
        Func<IReadOnlyList<string>, CancellationToken, Task> apply,
        CancellationToken cancellationToken = default)
    {
        CustomProfileDocument document;
        try
        {
            var json = await File.ReadAllTextAsync(request.ConfigPath, cancellationToken).ConfigureAwait(false);
            document = JsonSerializer.Deserialize<CustomProfileDocument>(json) ?? new CustomProfileDocument();
        }
        catch (Exception ex)
        {
            return new ConfigOutcome { Rejected = true, Summary = ex.Message };
        }

        var outcome = Plan(document, knownIds, request.AllowUnknown);
        if (!string.IsNullOrWhiteSpace(request.LogPath))
        {
            var dir = Path.GetDirectoryName(request.LogPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(request.LogPath, outcome.Summary, cancellationToken).ConfigureAwait(false);
        }

        if (request.WhatIf || !request.Apply || outcome.Rejected)
            return outcome;

        await apply(outcome.AcceptedIds, cancellationToken).ConfigureAwait(false);
        return outcome;
    }
}

using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WinCleaner.Core.Services;

public sealed record UpdateCheckResult(
    bool Success,
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl,
    string? ReleaseName,
    string? ErrorMessage);

/// <summary>
/// Checks GitHub Releases API for a newer version than the running assembly.
/// Releases: https://github.com/emirttac/WinCleaner
/// </summary>
public sealed class UpdateService
{
    /// <summary>GitHub user or org that owns the releases repository.</summary>
    public string GitHubOwner { get; init; } = "emirttac";

    /// <summary>Repository name that publishes Releases.</summary>
    public string GitHubRepo { get; init; } = "WinCleaner";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinCleaner", GetCurrentVersionString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string GetCurrentVersionString()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersionString();
        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(false, false, current, null, null, null,
                    $"No releases found for {GitHubOwner}/{GitHubRepo}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, false, current, null, null, null,
                    $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult(false, false, current, null, null, null,
                    "Could not parse GitHub release response.");
            }

            var latestRaw = release.TagName.Trim();
            var latestNormalized = NormalizeVersion(latestRaw);
            var currentNormalized = NormalizeVersion(current);
            var updateAvailable = IsNewer(latestNormalized, currentNormalized);

            return new UpdateCheckResult(
                Success: true,
                UpdateAvailable: updateAvailable,
                CurrentVersion: current,
                LatestVersion: latestRaw,
                ReleaseUrl: release.HtmlUrl,
                ReleaseName: release.Name,
                ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, current, null, null, null, ex.Message);
        }
    }

    internal static string NormalizeVersion(string value)
    {
        var s = value.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            s = s[1..];

        // Keep major.minor.patch[.revision] only
        var match = Regex.Match(s, @"^\d+(\.\d+){0,3}");
        return match.Success ? match.Value : s;
    }

    internal static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(PadVersion(latest), out var latestVer))
            return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);

        if (!Version.TryParse(PadVersion(current), out var currentVer))
            return true;

        return latestVer > currentVer;
    }

    private static string PadVersion(string v)
    {
        var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
        while (parts.Length < 2)
            v += ".0";
        // Version.TryParse needs at least major.minor; ensure 2–4 components
        parts = v.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0";
        return v;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}

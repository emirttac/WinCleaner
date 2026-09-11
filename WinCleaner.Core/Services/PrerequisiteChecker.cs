using System.Diagnostics;
using System.Net.Http;
using System.Security.Principal;
using Microsoft.Win32;

namespace WinCleaner.Core.Services;

public enum PrerequisiteKind
{
    OsVersion,
    Architecture,
    Administrator,
    DotNetDesktopRuntime,
    VcRedist
}

public sealed record PrerequisiteIssue(
    PrerequisiteKind Kind,
    string Title,
    string Detail,
    string? DownloadUrl,
    string? InstallerFileName,
    bool CanAutoInstall);

public sealed class PrerequisiteCheckResult
{
    public required IReadOnlyList<PrerequisiteIssue> Issues { get; init; }
    public bool HasBlockingIssues => Issues.Count > 0;
    public IEnumerable<PrerequisiteIssue> Installable =>
        Issues.Where(i => i.CanAutoInstall && !string.IsNullOrWhiteSpace(i.DownloadUrl));
}

/// <summary>
/// Checks OS / admin / .NET Desktop Runtime / VC++ redistributable before the UI loads.
/// </summary>
public static class PrerequisiteChecker
{
    public const int MinBuild = 19044; // Windows 10 21H2
    public const string DotNetDesktopUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";

    public static PrerequisiteCheckResult Check(bool includeDotNetIfFrameworkDependent = true)
    {
        var issues = new List<PrerequisiteIssue>();

        if (!Environment.Is64BitOperatingSystem)
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.Architecture,
                "64-bit Windows required",
                "WinCleaner only supports 64-bit Windows (x64).",
                DownloadUrl: null,
                InstallerFileName: null,
                CanAutoInstall: false));
        }

        var build = GetOsBuild();
        if (build < MinBuild)
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.OsVersion,
                "Windows version too old",
                $"WinCleaner needs Windows 10 21H2+ or Windows 11 (build {MinBuild}+). This PC reports build {build}.",
                DownloadUrl: null,
                InstallerFileName: null,
                CanAutoInstall: false));
        }

        if (!IsAdministrator())
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.Administrator,
                "Administrator required",
                "WinCleaner must run elevated. Right-click the exe and choose “Run as administrator”.",
                DownloadUrl: null,
                InstallerFileName: null,
                CanAutoInstall: false));
        }

        if (includeDotNetIfFrameworkDependent && IsFrameworkDependent() && !HasDotNet8Desktop())
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.DotNetDesktopRuntime,
                ".NET 8 Desktop Runtime missing",
                "This build needs the .NET 8 Desktop Runtime (x64). WinCleaner can download and install it for you.",
                DotNetDesktopUrl,
                "windowsdesktop-runtime-8.0-win-x64.exe",
                CanAutoInstall: true));
        }

        if (!IsVcRedistInstalled())
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.VcRedist,
                "Visual C++ Redistributable missing",
                "Microsoft Visual C++ 2015–2022 (x64) is required. WinCleaner can download and install it for you.",
                VcRedistUrl,
                "vc_redist.x64.exe",
                CanAutoInstall: true));
        }

        return new PrerequisiteCheckResult { Issues = issues };
    }

    public static async Task<bool> DownloadAndInstallAsync(
        PrerequisiteIssue issue,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!issue.CanAutoInstall || string.IsNullOrWhiteSpace(issue.DownloadUrl))
            return false;

        var fileName = string.IsNullOrWhiteSpace(issue.InstallerFileName)
            ? "prerequisite-setup.exe"
            : issue.InstallerFileName!;
        var path = Path.Combine(Path.GetTempPath(), "WinCleaner", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        progress?.Report($"Downloading {issue.Title}…");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinCleaner/1.0");
            await using var remote = await http.GetStreamAsync(issue.DownloadUrl, ct).ConfigureAwait(false);
            await using var local = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await remote.CopyToAsync(local, ct).ConfigureAwait(false);
        }

        progress?.Report($"Installing {issue.Title}…");
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = "/install /quiet /norestart",
            UseShellExecute = true,
            Verb = "runas"
        };

        using var proc = Process.Start(psi);
        if (proc is null)
            return false;

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        // 0 = success, 1638/3010 = already installed / reboot required — treat as OK enough to continue
        return proc.ExitCode is 0 or 1638 or 3010 or 1641;
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static int GetOsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildStr = key?.GetValue("CurrentBuildNumber")?.ToString()
                           ?? key?.GetValue("CurrentBuild")?.ToString();
            if (int.TryParse(buildStr, out var build))
                return build;
        }
        catch { }

        return 0;
    }

    public static bool HasDotNet8Desktop()
    {
        var bases = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "shared", "Microsoft.WindowsDesktop.App")
        };

        foreach (var root in bases)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var dir in Directory.EnumerateDirectories(root, "8.*"))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("8.", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public static bool IsVcRedistInstalled()
    {
        string[] paths =
        [
            @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
            @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
        ];

        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key is null) continue;
                var installed = key.GetValue("Installed");
                if (installed is int i && i == 1)
                    return true;
                if (installed is string s && s == "1")
                    return true;
                // Major version 14 with any Bld is enough
                if (key.GetValue("Major") is not null)
                    return true;
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// True when this process is using the shared .NET install (framework-dependent publish).
    /// Self-contained / single-file bundles the runtime and returns false.
    /// </summary>
    public static bool IsFrameworkDependent()
    {
        try
        {
            // Single-file publishes report empty Location for embedded assemblies.
#pragma warning disable IL3000
            var coreLoc = typeof(object).Assembly.Location;
            var entryLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
#pragma warning restore IL3000

            if (string.IsNullOrWhiteSpace(coreLoc) && string.IsNullOrWhiteSpace(entryLoc))
                return false;

            if (!string.IsNullOrWhiteSpace(coreLoc)
                && coreLoc.Contains("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrWhiteSpace(entryLoc))
                return false;

            var dir = Path.GetDirectoryName(entryLoc);
            if (dir is not null
                && File.Exists(Path.Combine(dir, "hostfxr.dll"))
                && !File.Exists(Path.Combine(dir, "WinCleaner.dll")))
            {
                return HasDotNet8Desktop();
            }
        }
        catch { }

        return false;
    }
}

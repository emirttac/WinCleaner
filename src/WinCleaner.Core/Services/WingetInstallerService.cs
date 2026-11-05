using System.Diagnostics;
using System.Text;
using WinCleaner.Core.Abstractions;

namespace WinCleaner.Core.Services;

public enum WingetInstallStatus
{
    Idle,
    Queued,
    Starting,
    Installing,
    Completed,
    Failed,
    AlreadyInstalled,
    Cancelled
}

public sealed record WingetAvailability(bool Available, string? Version, string? ErrorMessage);

public sealed record WingetInstallResult(
    bool Success,
    WingetInstallStatus Status,
    int ExitCode,
    string Output,
    string? ErrorMessage);

/// <summary>
/// Detects winget and runs silent installs: winget install --id … -e --silent …
/// </summary>
public sealed class WingetInstallerService
{
    private readonly IProcessRunner _processes;

    public WingetInstallerService(IProcessRunner? processes = null)
    {
        _processes = processes ?? ProcessRunner.Shared;
    }

    public async Task<WingetAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            var exe = ResolveWingetPath();
            if (exe is null)
            {
                return new WingetAvailability(false, null,
                    "winget was not found. Install «App Installer» from the Microsoft Store.");
            }

            var run = await RunProcessAsync(exe, "--version", ct).ConfigureAwait(false);
            if (!run.Success)
            {
                return new WingetAvailability(false, null,
                    run.ErrorMessage ?? "winget --version failed.");
            }

            var version = (run.StdOut + " " + run.StdErr).Trim();
            if (string.IsNullOrWhiteSpace(version))
                version = "unknown";

            return new WingetAvailability(true, version.Split('\n', '\r')[0].Trim(), null);
        }
        catch (Exception ex)
        {
            return new WingetAvailability(false, null, ex.Message);
        }
    }

    public const string AppInstallerStoreDeepLink = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1";

    /// <summary>Opens the Microsoft Store page for App Installer (provides winget).</summary>
    public bool OpenAppInstallerStore()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppInstallerStoreDeepLink,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://apps.microsoft.com/detail/9nblggh4nns1",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Downloads and installs the latest Microsoft.DesktopAppInstaller msixbundle from GitHub releases.
    /// </summary>
    public async Task<(bool Success, string Message)> TryBootstrapWingetAsync(CancellationToken ct = default)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WinCleaner-winget-bootstrap");
            Directory.CreateDirectory(tempDir);
            var bundlePath = Path.Combine(tempDir, "Microsoft.DesktopAppInstaller.msixbundle");

            var apiRun = await RunProcessAsync(
                "powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                "$ProgressPreference='SilentlyContinue'; " +
                "$r = Invoke-RestMethod -Uri 'https://api.github.com/repos/microsoft/winget-cli/releases/latest' -Headers @{ 'User-Agent'='WinCleaner' }; " +
                "$a = $r.assets | Where-Object { $_.name -like '*.msixbundle' } | Select-Object -First 1; " +
                "if (-not $a) { throw 'No msixbundle in latest release' }; " +
                "Write-Output $a.browser_download_url\"",
                ct,
                timeoutMs: 120_000).ConfigureAwait(false);

            var url = (apiRun.StdOut ?? "").Trim().Split('\n', '\r')[0].Trim();
            if (!apiRun.Success || string.IsNullOrWhiteSpace(url))
            {
                var err = apiRun.ErrorMessage;
                if (string.IsNullOrWhiteSpace(err))
                    err = string.IsNullOrWhiteSpace(apiRun.StdErr) ? "Could not resolve winget release URL." : apiRun.StdErr.Trim();
                return (false, err);
            }

            var download = await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri '{url.Replace("'", "''")}' -OutFile '{bundlePath.Replace("'", "''")}'\"",
                ct,
                timeoutMs: 10 * 60 * 1000).ConfigureAwait(false);

            if (!download.Success || !File.Exists(bundlePath))
                return (false, download.ErrorMessage ?? "Failed to download App Installer package.");

            var install = await RunProcessAsync(
                "powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{bundlePath.Replace("'", "''")}' -ForceApplicationShutdown\"",
                ct,
                timeoutMs: 5 * 60 * 1000).ConfigureAwait(false);

            if (!install.Success)
            {
                var err = install.ErrorMessage;
                if (string.IsNullOrWhiteSpace(err))
                    err = string.IsNullOrWhiteSpace(install.StdErr)
                        ? $"Add-AppxPackage failed ({install.ExitCode})."
                        : install.StdErr.Trim();
                return (false, err);
            }

            return (true, "App Installer installed.");
        }
        catch (OperationCanceledException)
        {
            return (false, "Cancelled");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<WingetInstallResult> UpgradeAsync(
        string wingetId,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        await RunWingetPackageCommandAsync(wingetId, "upgrade", "installing", progress, ct).ConfigureAwait(false);

    public async Task<IReadOnlySet<string>> FindInstalledAsync(
        IEnumerable<string> wingetIds,
        CancellationToken ct = default)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exe = ResolveWingetPath();
        if (exe is null)
            return found;

        var run = await RunProcessAsync(exe, "list --accept-source-agreements", ct, timeoutMs: 120_000).ConfigureAwait(false);
        var text = run.StdOut + Environment.NewLine + run.StdErr;
        foreach (var id in wingetIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            if (text.Contains(id, StringComparison.OrdinalIgnoreCase))
                found.Add(id);
        }

        return found;
    }

    public async Task<WingetInstallResult> InstallAsync(
        string wingetId,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        await RunWingetPackageCommandAsync(
            wingetId,
            "install",
            progressPhase: "installing",
            progress,
            ct).ConfigureAwait(false);

    public async Task<WingetInstallResult> UninstallAsync(
        string wingetId,
        IProgress<string>? progress = null,
        CancellationToken ct = default) =>
        await RunWingetPackageCommandAsync(
            wingetId,
            "uninstall",
            progressPhase: "installing",
            progress,
            ct).ConfigureAwait(false);

    private async Task<WingetInstallResult> RunWingetPackageCommandAsync(
        string wingetId,
        string verb,
        string progressPhase,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(wingetId))
            return Fail(WingetInstallStatus.Failed, "Winget ID is empty.");

        var exe = ResolveWingetPath();
        if (exe is null)
            return Fail(WingetInstallStatus.Failed,
                "winget was not found. Install «App Installer» from the Microsoft Store.");

        progress?.Report("starting");
        var args = verb.Equals("uninstall", StringComparison.OrdinalIgnoreCase)
            ? $"uninstall --id {Quote(wingetId)} -e --silent --disable-interactivity --accept-source-agreements"
            : $"install --id {Quote(wingetId)} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

        if (verb.Equals("upgrade", StringComparison.OrdinalIgnoreCase))
            args = $"upgrade --id {Quote(wingetId)} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

        try
        {
            progress?.Report(progressPhase);
            var run = await RunProcessAsync(exe, args, ct, timeoutMs: 30 * 60 * 1000).ConfigureAwait(false);
            var combined = (run.StdOut + Environment.NewLine + run.StdErr).Trim();

            // winget exit codes: 0 success; -1978335189 / 0x8A15002B already installed (varies by version)
            if (run.Success || IsAlreadyInstalled(run.ExitCode, combined) || IsAlreadyAbsent(run.ExitCode, combined, verb))
            {
                var status = IsAlreadyInstalled(run.ExitCode, combined)
                    ? WingetInstallStatus.AlreadyInstalled
                    : WingetInstallStatus.Completed;
                progress?.Report(status == WingetInstallStatus.AlreadyInstalled ? "already" : "completed");
                return new WingetInstallResult(true, status, run.ExitCode, combined, null);
            }

            progress?.Report("failed");
            return new WingetInstallResult(false, WingetInstallStatus.Failed, run.ExitCode, combined,
                string.IsNullOrWhiteSpace(combined) ? $"winget exit {run.ExitCode}" : Truncate(combined, 400));
        }
        catch (OperationCanceledException)
        {
            progress?.Report("cancelled");
            return new WingetInstallResult(false, WingetInstallStatus.Cancelled, -1, "", "Cancelled");
        }
        catch (Exception ex)
        {
            progress?.Report("failed");
            return Fail(WingetInstallStatus.Failed, ex.Message);
        }
    }

    private static bool IsAlreadyAbsent(int exitCode, string output, string verb)
    {
        if (!verb.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
            return false;
        // Common winget codes / messages for "no installed package found"
        if (exitCode is -1978335212 or unchecked((int)0x8A150014))
            return true;
        return output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase)
               || output.Contains("not installed", StringComparison.OrdinalIgnoreCase);
    }

    private static WingetInstallResult Fail(WingetInstallStatus status, string message) =>
        new(false, status, -1, "", message);

    private static bool IsAlreadyInstalled(int exitCode, string output)
    {
        // Common winget codes / messages for "already installed"
        if (exitCode is -1978335189 or unchecked((int)0x8A15002B) or -1978335135)
            return true;
        return output.Contains("already installed", StringComparison.OrdinalIgnoreCase)
               || output.Contains("No newer package versions are available", StringComparison.OrdinalIgnoreCase)
               || output.Contains("zaten yüklü", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveWingetPath()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(local))
            return local;

        var pf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "WindowsApps");
        // WindowsApps is ACL-restricted; PATH lookup is the reliable fallback.
        return "winget";
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private async Task<(bool Success, int ExitCode, string StdOut, string StdErr, string? ErrorMessage)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken ct,
        int timeoutMs = 60000)
    {
        try
        {
            var result = await _processes.RunAsync(
                new ProcessRequest(fileName, arguments, TimeSpan.FromMilliseconds(timeoutMs)),
                ct).ConfigureAwait(false);
            if (result.Cancelled)
                throw new OperationCanceledException(ct);
            if (result.TimedOut || result.ExitCode is null)
                return (false, -1, result.Stdout, result.Stderr, "winget timed out.");
            return (result.ExitCode == 0, result.ExitCode.Value, result.Stdout, result.Stderr, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, -1, "", "", ex.Message);
        }
    }
}

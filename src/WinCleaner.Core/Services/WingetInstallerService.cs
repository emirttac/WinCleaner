using System.Diagnostics;
using System.Text;

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

    public async Task<WingetInstallResult> InstallAsync(
        string wingetId,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(wingetId))
            return Fail(WingetInstallStatus.Failed, "Winget ID is empty.");

        var exe = ResolveWingetPath();
        if (exe is null)
            return Fail(WingetInstallStatus.Failed,
                "winget was not found. Install «App Installer» from the Microsoft Store.");

        progress?.Report("starting");
        var args =
            $"install --id {Quote(wingetId)} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

        try
        {
            progress?.Report("installing");
            var run = await RunProcessAsync(exe, args, ct, timeoutMs: 30 * 60 * 1000).ConfigureAwait(false);
            var combined = (run.StdOut + Environment.NewLine + run.StdErr).Trim();

            // winget exit codes: 0 success; -1978335189 / 0x8A15002B already installed (varies by version)
            if (run.Success || IsAlreadyInstalled(run.ExitCode, combined))
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

    private static async Task<(bool Success, int ExitCode, string StdOut, string StdErr, string? ErrorMessage)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken ct,
        int timeoutMs = 60000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };
        process.Exited += (_, _) =>
        {
            try { tcs.TrySetResult(process.ExitCode); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        };

        try
        {
            if (!process.Start())
                return (false, -1, "", "", $"Failed to start {fileName}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await using (ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { }
                tcs.TrySetCanceled(ct);
            }).ConfigureAwait(false))
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (completed != tcs.Task)
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return (false, -1, stdout.ToString(), stderr.ToString(), "winget timed out.");
                }

                var code = await tcs.Task.ConfigureAwait(false);
                return (code == 0, code, stdout.ToString(), stderr.ToString(), null);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, -1, stdout.ToString(), stderr.ToString(), ex.Message);
        }
    }
}

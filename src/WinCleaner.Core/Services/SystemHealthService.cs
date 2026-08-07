using System.Diagnostics;
using Microsoft.Win32;

namespace WinCleaner.Core.Services;

/// <summary>
/// DISM/SFC health repair, WinSxS cleanup, Take Ownership context menu, and God Mode folder.
/// </summary>
public sealed class SystemHealthService
{
    private const string TakeOwnFileKey = @"*\shell\runas";
    private const string TakeOwnDirKey = @"Directory\shell\runas";
    private const string GodModeFolderName = "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}";

    public async Task RunDismRestoreHealthAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report(">>> DISM /Online /Cleanup-Image /RestoreHealth");
        var code = await RunStreamingAsync(
            "DISM.exe",
            "/Online /Cleanup-Image /RestoreHealth",
            progress,
            ct).ConfigureAwait(false);
        progress?.Report($"<<< DISM finished (exit {code})");
        if (code != 0)
            throw new InvalidOperationException($"DISM RestoreHealth failed with exit code {code}.");
    }

    public async Task RunSfcScannowAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report(">>> sfc /scannow");
        var code = await RunStreamingAsync(
            "sfc.exe",
            "/scannow",
            progress,
            ct).ConfigureAwait(false);
        progress?.Report($"<<< SFC finished (exit {code})");
        if (code != 0)
            throw new InvalidOperationException($"sfc /scannow failed with exit code {code}.");
    }

    /// <summary>Runs DISM RestoreHealth, then SFC /scannow, streaming all output.</summary>
    public async Task RunSfcAndDismRepairAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("=== System file repair started ===");
        progress?.Report($"Admin: {PrivilegeHelper.IsAdministrator()}");
        if (!PrivilegeHelper.IsAdministrator())
            throw new UnauthorizedAccessException("Administrator privileges are required for DISM/SFC.");

        await RunDismRestoreHealthAsync(progress, ct).ConfigureAwait(false);
        progress?.Report("");
        await RunSfcScannowAsync(progress, ct).ConfigureAwait(false);
        progress?.Report("=== System file repair completed ===");
    }

    public async Task RunComponentStoreCleanupAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("=== WinSxS / Component Store cleanup ===");
        progress?.Report(">>> DISM /Online /Cleanup-Image /StartComponentCleanup /ResetBase");
        if (!PrivilegeHelper.IsAdministrator())
            throw new UnauthorizedAccessException("Administrator privileges are required for component cleanup.");

        var code = await RunStreamingAsync(
            "DISM.exe",
            "/Online /Cleanup-Image /StartComponentCleanup /ResetBase",
            progress,
            ct).ConfigureAwait(false);
        progress?.Report($"<<< DISM Component Cleanup finished (exit {code})");
        if (code != 0)
            throw new InvalidOperationException($"DISM StartComponentCleanup failed with exit code {code}.");
        progress?.Report("=== Component Store cleanup completed ===");
    }

    public string CreateGodModeFolder(string? desktopPath = null)
    {
        var desktop = desktopPath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            throw new DirectoryNotFoundException("Desktop folder was not found.");

        var path = Path.Combine(desktop, GodModeFolderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public bool IsTakeOwnershipMenuEnabled()
    {
        try
        {
            using var fileCmd = Registry.ClassesRoot.OpenSubKey(TakeOwnFileKey + @"\command");
            using var dirCmd = Registry.ClassesRoot.OpenSubKey(TakeOwnDirKey + @"\command");
            return fileCmd is not null && dirCmd is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the classic "Take Ownership" context menu entries under HKCR * and Directory.
    /// </summary>
    public void SetTakeOwnershipMenu(bool enabled, string menuLabel)
    {
        if (string.IsNullOrWhiteSpace(menuLabel))
            menuLabel = "Take Ownership";

        if (!enabled)
        {
            DeleteKeyTree(TakeOwnFileKey);
            DeleteKeyTree(TakeOwnDirKey);
            return;
        }

        // Files
        WriteTakeOwnKeys(
            TakeOwnFileKey,
            menuLabel,
            "cmd.exe /c takeown /f \"%1\" && icacls \"%1\" /grant administrators:F");

        // Folders (recursive)
        WriteTakeOwnKeys(
            TakeOwnDirKey,
            menuLabel,
            "cmd.exe /c takeown /f \"%1\" /r /d y && icacls \"%1\" /grant administrators:F /t");
    }

    private static void WriteTakeOwnKeys(string shellKey, string label, string command)
    {
        using (var key = Registry.ClassesRoot.CreateSubKey(shellKey, true)
               ?? throw new InvalidOperationException($"Cannot create HKCR\\{shellKey}"))
        {
            key.SetValue("", label);
            key.SetValue("NoWorkingDirectory", "");
            key.SetValue("HasLUAShield", "");
        }

        using var cmd = Registry.ClassesRoot.CreateSubKey(shellKey + @"\command", true)
            ?? throw new InvalidOperationException($"Cannot create HKCR\\{shellKey}\\command");
        cmd.SetValue("", command);
        cmd.SetValue("IsolatedCommand", command);
    }

    /// <summary>
    /// Classic Windows Update reset: stop services, rename SoftwareDistribution/catroot2, restart services.
    /// </summary>
    public async Task ResetWindowsUpdateAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("=== Windows Update reset ===");
        if (!PrivilegeHelper.IsAdministrator())
            throw new UnauthorizedAccessException("Administrator privileges are required for Windows Update reset.");

        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sd = Path.Combine(winDir, "SoftwareDistribution");
        var sdBak = Path.Combine(winDir, "SoftwareDistribution.bak");
        var cat = Path.Combine(winDir, "System32", "catroot2");
        var catBak = Path.Combine(winDir, "System32", "catroot2.bak");

        foreach (var svc in new[] { "wuauserv", "bits", "cryptsvc" })
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"> net stop {svc}");
            await RunStreamingAsync("net.exe", $"stop {svc}", progress, ct).ConfigureAwait(false);
        }

        TryRenameFolder(sd, sdBak, progress);
        TryRenameFolder(cat, catBak, progress);

        foreach (var svc in new[] { "wuauserv", "bits", "cryptsvc" })
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"> net start {svc}");
            await RunStreamingAsync("net.exe", $"start {svc}", progress, ct).ConfigureAwait(false);
        }

        progress?.Report("=== Windows Update reset completed ===");
    }

    /// <summary>Winsock + IP stack reset. Caller should prompt for reboot.</summary>
    public async Task ResetNetworkStackAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("=== Network stack reset (Winsock / IP) ===");
        if (!PrivilegeHelper.IsAdministrator())
            throw new UnauthorizedAccessException("Administrator privileges are required for network reset.");

        progress?.Report("> netsh winsock reset");
        var code1 = await RunStreamingAsync("netsh.exe", "winsock reset", progress, ct).ConfigureAwait(false);
        progress?.Report($"<<< winsock reset (exit {code1})");

        progress?.Report("> netsh int ip reset");
        var code2 = await RunStreamingAsync("netsh.exe", "int ip reset", progress, ct).ConfigureAwait(false);
        progress?.Report($"<<< ip reset (exit {code2})");

        if (code1 != 0 || code2 != 0)
            throw new InvalidOperationException($"Network reset finished with errors (winsock={code1}, ip={code2}).");

        progress?.Report("=== Network reset completed — reboot recommended ===");
    }

    /// <summary>Lists top-level folder sizes under the given drive root (fast overview).</summary>
    public Task AnalyzeDiskSpaceAsync(string driveRoot, IProgress<string>? progress, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            progress?.Report("=== Disk space analysis ===");
            if (string.IsNullOrWhiteSpace(driveRoot))
                driveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

            driveRoot = Path.GetFullPath(driveRoot);
            if (!driveRoot.EndsWith(Path.DirectorySeparatorChar) && !driveRoot.EndsWith(Path.AltDirectorySeparatorChar))
                driveRoot += Path.DirectorySeparatorChar;

            if (!Directory.Exists(driveRoot))
                throw new DirectoryNotFoundException($"Drive not found: {driveRoot}");

            try
            {
                var di = new DriveInfo(driveRoot);
                progress?.Report($"{di.Name}  total={FormatBytes(di.TotalSize)}  free={FormatBytes(di.AvailableFreeSpace)}");
            }
            catch { /* best effort */ }

            progress?.Report("");
            progress?.Report("Top-level folders (largest first):");

            var rows = new List<(string Name, long Size)>();
            foreach (var dir in Directory.EnumerateDirectories(driveRoot))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                progress?.Report($"  scanning {name}…");
                var size = SafeDirSize(dir, ct);
                rows.Add((name, size));
            }

            long fileSum = 0;
            foreach (var file in Directory.EnumerateFiles(driveRoot))
            {
                ct.ThrowIfCancellationRequested();
                try { fileSum += new FileInfo(file).Length; } catch { }
            }

            if (fileSum > 0)
                rows.Add(("(root files)", fileSum));

            foreach (var row in rows.OrderByDescending(r => r.Size))
                progress?.Report($"  {FormatBytes(row.Size).PadLeft(10)}  {row.Name}");

            progress?.Report("");
            progress?.Report("=== Disk analysis completed ===");
        }, ct);
    }

    private static void TryRenameFolder(string source, string dest, IProgress<string>? progress)
    {
        progress?.Report($"> rename {source} → {Path.GetFileName(dest)}");
        try
        {
            if (!Directory.Exists(source))
            {
                progress?.Report($"  (skip — not found)");
                return;
            }

            if (Directory.Exists(dest))
            {
                var stamped = dest + "." + DateTime.Now.ToString("yyyyMMddHHmmss");
                progress?.Report($"  existing backup → {Path.GetFileName(stamped)}");
                Directory.Move(dest, stamped);
            }

            Directory.Move(source, dest);
            progress?.Report("  OK");
        }
        catch (Exception ex)
        {
            progress?.Report($"  WARN: {ex.Message}");
        }
    }

    private static long SafeDirSize(string path, CancellationToken ct)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        return total;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }

    private static void DeleteKeyTree(string relativePath)
    {
        try
        {
            Registry.ClassesRoot.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort remove
        }
    }

    public static async Task<int> RunStreamingAsync(
        string fileName,
        string arguments,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var exe = ResolveSystemExe(fileName);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
            // Intentionally use OS default encoding — DISM/SFC emit OEM/ANSI console text.
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            progress?.Report(e.Data);
        }

        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;
        process.Exited += (_, _) =>
        {
            try { tcs.TrySetResult(process.ExitCode); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start {exe}");

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
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private static string ResolveSystemExe(string fileName)
    {
        if (Path.IsPathRooted(fileName))
            return fileName;

        var leaf = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".exe";
        var full = Path.Combine(Environment.SystemDirectory, leaf);
        return File.Exists(full) ? full : fileName;
    }
}

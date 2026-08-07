using System.Diagnostics;
using System.Management;

namespace WinCleaner.Core.Services;

public sealed class RestorePointManager
{
    private static readonly object Gate = new();
    private static DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;

    /// <summary>
    /// Minimum gap between restore points. Windows rate-limits checkpoints;
    /// calling too often freezes or fails and was hanging the UI.
    /// </summary>
    public static TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(10);

    public bool ShouldCreateNow()
    {
        lock (Gate)
            return DateTimeOffset.UtcNow - _lastSuccess >= MinInterval;
    }

    public Task<bool> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateRestorePoint(description), cancellationToken);

    public bool CreateRestorePoint(string description)
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - _lastSuccess < MinInterval)
                return true; // skip; treat as ok so callers don't block
        }

        try
        {
            var ok = TryWmi(description) || TryPowerShell(description);
            if (ok)
            {
                lock (Gate)
                    _lastSuccess = DateTimeOffset.UtcNow;
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWmi(string description)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            scope.Connect();

            using var managementClass = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var parameters = managementClass.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description.Length > 256 ? description[..256] : description;
            parameters["RestorePointType"] = 12; // MODIFY_SETTINGS
            parameters["EventType"] = 100; // BEGIN_SYSTEM_CHANGE

            using var result = managementClass.InvokeMethod("CreateRestorePoint", parameters, null);
            return Convert.ToInt32(result["ReturnValue"]) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPowerShell(string description)
    {
        try
        {
            var script = $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' -RestorePointType MODIFY_SETTINGS";
            var bytes = System.Text.Encoding.Unicode.GetBytes(script);
            var encoded = Convert.ToBase64String(bytes);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            // Keep timeout short so a hung Checkpoint-Computer cannot freeze the app for minutes
            if (!proc.WaitForExit(20_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

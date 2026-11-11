using System.Diagnostics;
using System.Management;

namespace WinCleaner.Core.Services;

public enum RestorePointOutcome
{
    Created,
    /// <summary>
    /// A WinCleaner point is already listed. Windows often returns success and then
    /// skips a new point for about 24 hours, so this is not a failure and not "protection off".
    /// </summary>
    AlreadyExists,
    NotCreated,
    ProtectionDisabled
}

public readonly record struct RestorePointRow(int Sequence, string Description);

/// <summary>
/// Creates at most one System Restore checkpoint per WinCleaner session (at startup).
/// Per-tweak checkpoints hit Windows rate limits and freeze the UI — never do that.
/// </summary>
public sealed class RestorePointManager
{
    private static readonly object Gate = new();
    private static DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;

    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private bool _autoAttempted;

    /// <summary>
    /// Minimum gap between restore points. Windows rate-limits checkpoints;
    /// calling too often freezes or fails and was hanging the UI.
    /// </summary>
    public static TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>True after a restore point was created (or confirmed) in this app session.</summary>
    public bool HasSessionCheckpoint { get; private set; }

    /// <summary>UI hook: show a warning when the user starts work without a session restore point.</summary>
    public Func<Task>? NotifyMissingSessionCheckpoint { get; set; }

    public event Action? SessionCheckpointChanged;

    /// <summary>Test hook — when set, WMI/PowerShell are not called.</summary>
    internal static Func<string, bool>? CreateCoreOverride { get; set; }

    public bool ShouldCreateNow()
    {
        lock (Gate)
            return DateTimeOffset.UtcNow - _lastSuccess >= MinInterval;
    }

    public Task<bool> CreateRestorePointAsync(string description, CancellationToken cancellationToken = default) =>
        CreateRestorePointAsync(description, ignoreMinInterval: false, cancellationToken);

    public Task<bool> CreateRestorePointAsync(string description, bool ignoreMinInterval, CancellationToken cancellationToken = default) =>
        Task.Run(() => CreateRestorePoint(description, ignoreMinInterval), cancellationToken);

    public bool CreateRestorePoint(string description, bool ignoreMinInterval = false)
    {
        if (!ignoreMinInterval && !ShouldCreateNow())
            return HasSessionCheckpoint;

        var outcome = CreateChecked(description);
        return outcome is RestorePointOutcome.Created or RestorePointOutcome.AlreadyExists;
    }

    public static RestorePointOutcome LastOutcome { get; private set; }

    public RestorePointOutcome CreateChecked(string description)
    {
        if (ProtectionOverride?.Invoke() == false)
        {
            LastOutcome = RestorePointOutcome.ProtectionDisabled;
            return LastOutcome;
        }

        var before = ReadPoints();
        int? wmiCode = null;
        var apiOk = false;
        try
        {
            if (CreateCoreOverride is not null)
            {
                apiOk = CreateCoreOverride(description);
            }
            else
            {
                wmiCode = TryWmi(description);
                apiOk = wmiCode == 0 || TryPowerShell(description);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Restore point create failed: " + ex.Message);
        }

        if (ListOverride is null && CreateCoreOverride is not null)
        {
            if (!apiOk)
            {
                LastOutcome = RestorePointOutcome.NotCreated;
                return LastOutcome;
            }
            lock (Gate)
                _lastSuccess = DateTimeOffset.UtcNow;
            MarkSessionCheckpoint();
            LastOutcome = RestorePointOutcome.Created;
            return LastOutcome;
        }

        var after = ReadAfter(before, description);
        if (!HasNewPoint(before, after, description))
        {
            if (HasLabel(before, description) || HasLabel(after, description))
            {
                MarkSessionCheckpoint();
                LastOutcome = RestorePointOutcome.AlreadyExists;
                return LastOutcome;
            }

            if (ListOverride is null && CreateCoreOverride is null && IsProtectionDisabled(wmiCode))
            {
                LastOutcome = RestorePointOutcome.ProtectionDisabled;
                return LastOutcome;
            }

            LastOutcome = RestorePointOutcome.NotCreated;
            return LastOutcome;
        }

        lock (Gate)
            _lastSuccess = DateTimeOffset.UtcNow;
        MarkSessionCheckpoint();
        LastOutcome = RestorePointOutcome.Created;
        return LastOutcome;
    }

    private static bool HasLabel(IReadOnlyList<RestorePointRow> points, string description) =>
        points.Any(point => point.Description == description);

    private static bool HasNewPoint(IReadOnlyList<RestorePointRow> before, IReadOnlyList<RestorePointRow> after, string description) =>
        after.Any(point =>
            point.Description == description &&
            !before.Any(previous => previous.Sequence == point.Sequence));

    /// <summary>
    /// The SystemRestore list can lag the create call. On a live PC, wait briefly for a new
    /// WinCleaner row. Skip the wait when a WinCleaner row was already there: Windows did not
    /// add one, and an 8 second poll would only delay the "existing point" result.
    /// </summary>
    private static IReadOnlyList<RestorePointRow> ReadAfter(IReadOnlyList<RestorePointRow> before, string description)
    {
        var after = ReadPoints();
        if (ListOverride is not null || HasLabel(before, description) || HasNewPoint(before, after, description))
            return after;

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            after = ReadPoints();
            if (HasNewPoint(before, after, description))
                break;
        }

        return after;
    }

    private static IReadOnlyList<RestorePointRow> ReadPoints() =>
        ListOverride?.Invoke() ?? ReadLivePoints();

    private static IReadOnlyList<RestorePointRow> ReadLivePoints()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\default", "SELECT SequenceNumber, Description FROM SystemRestore");
            var rows = new List<RestorePointRow>();
            foreach (ManagementObject item in searcher.Get())
            {
                var sequence = Convert.ToInt32(item["SequenceNumber"] ?? 0);
                var description = item["Description"]?.ToString() ?? "";
                rows.Add(new RestorePointRow(sequence, description));
            }
            return rows;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Restore point list failed: " + ex.Message);
            return Array.Empty<RestorePointRow>();
        }
    }

    internal static Func<IReadOnlyList<RestorePointRow>>? ListOverride { get; set; }
    internal static Func<bool>? ProtectionOverride { get; set; }

    /// <summary>User clicked "Create restore point" — always try, mark session on success.</summary>
    public Task<bool> CreateManualAsync(string description, CancellationToken cancellationToken = default) =>
        CreateRestorePointAsync(description, ignoreMinInterval: true, cancellationToken);

    /// <summary>
    /// One restore point per session. If the user skipped the manual button,
    /// optionally warn (UI callback) then create automatically. Never creates per tweak.
    /// </summary>
    public async Task EnsureSessionCheckpointAsync(
        bool enabled,
        bool warnIfMissing,
        CancellationToken cancellationToken = default)
    {
        if (!enabled || HasSessionCheckpoint)
            return;

        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (HasSessionCheckpoint)
                return;

            if (_autoAttempted)
                return;

            if (warnIfMissing && NotifyMissingSessionCheckpoint is not null)
            {
                try
                {
                    await NotifyMissingSessionCheckpoint().ConfigureAwait(false);
                }
                catch
                {
                    // Warning UI must not block the actual work
                }
            }

            _autoAttempted = true;
            try
            {
                await CreateRestorePointAsync("WinCleaner", ignoreMinInterval: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("Session restore point failed: " + ex.Message);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public void MarkSessionCheckpoint()
    {
        HasSessionCheckpoint = true;
        SessionCheckpointChanged?.Invoke();
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            _lastSuccess = DateTimeOffset.MinValue;
        CreateCoreOverride = null;
        ListOverride = null;
        ProtectionOverride = null;
        MinInterval = TimeSpan.FromMinutes(10);
    }

    private static int? TryWmi(string description)
    {
        try
        {
            var task = Task.Run(() => TryWmiCore(description));
            if (!task.Wait(TimeSpan.FromSeconds(45)))
                return null;
            return task.Status == TaskStatus.RanToCompletion ? task.Result : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryWmiCore(string description)
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
            return Convert.ToInt32(result["ReturnValue"]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>1058 is ERROR_SERVICE_DISABLED. DisableSR is the system-wide protection switch.</summary>
    private static bool IsProtectionDisabled(int? wmiCode)
    {
        if (wmiCode == 1058)
            return true;

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
            return key?.GetValue("DisableSR") is int disabled && disabled != 0;
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
            // Dialog copy says this can take about a minute. 20s used to kill a point that
            // still showed up afterwards in Windows Settings.
            if (!proc.WaitForExit(90_000))
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

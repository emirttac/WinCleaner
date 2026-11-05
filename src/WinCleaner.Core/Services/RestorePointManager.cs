using System.Diagnostics;
using System.Management;

namespace WinCleaner.Core.Services;

public enum RestorePointOutcome
{
    Created,
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
        return outcome == RestorePointOutcome.Created;
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
        bool apiOk;
        try
        {
            apiOk = CreateCoreOverride?.Invoke(description) ?? (TryWmi(description) || TryPowerShell(description));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Restore point create failed: " + ex.Message);
            apiOk = false;
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

        var after = ReadPoints();
        var created = after.Any(point =>
            point.Description == description &&
            !before.Any(previous => previous.Sequence == point.Sequence));
        if (!created)
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

    private static bool TryWmi(string description)
    {
        try
        {
            var task = Task.Run(() => TryWmiCore(description));
            if (!task.Wait(TimeSpan.FromSeconds(45)))
                return false;
            return task.Status == TaskStatus.RanToCompletion && task.Result;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWmiCore(string description)
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

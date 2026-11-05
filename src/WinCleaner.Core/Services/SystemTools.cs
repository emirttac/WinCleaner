using WinCleaner.Core.Abstractions;

namespace WinCleaner.Core.Services;

public sealed class TaskSchedulerManager
{
    private readonly IProcessRunner _processes;

    public TaskSchedulerManager(IProcessRunner? processes = null)
    {
        _processes = processes ?? ProcessRunner.Shared;
    }

    public void SetTaskEnabled(string taskPath, bool enabled)
    {
        var args = enabled
            ? $"/Change /TN \"{taskPath}\" /Enable"
            : $"/Change /TN \"{taskPath}\" /Disable";
        RunSchtasks(args);
    }

    public bool TaskExists(string taskPath)
    {
        var output = RunSchtasks($"/Query /TN \"{taskPath}\" /XML");
        return TryReadEnabled(output, out _);
    }

    public bool IsTaskEnabled(string taskPath)
    {
        try
        {
            var output = RunSchtasks($"/Query /TN \"{taskPath}\" /XML");
            var state = ParseScheduledTaskState(output);
            return state switch
            {
                TaskEnablement.Disabled => false,
                TaskEnablement.Missing => false,
                // Unknown: assume still enabled so the UI does not look "applied"
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }

    internal static TaskEnablement ParseScheduledTaskState(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return TaskEnablement.Unknown;
        if (TryReadEnabled(output, out var enabled))
            return enabled ? TaskEnablement.Enabled : TaskEnablement.Disabled;
        if (output.Contains("<Task", StringComparison.OrdinalIgnoreCase))
            return TaskEnablement.Unknown;
        return TaskEnablement.Missing;
    }

    internal static bool LooksMissing(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;
        return !output.Contains("<Task", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryReadEnabled(string output, out bool enabled)
    {
        enabled = false;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            output,
            @"<Enabled>\s*(true|false)\s*</Enabled>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        enabled = match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private string RunSchtasks(string args)
    {
        var result = _processes.RunAsync(
            new ProcessRequest("schtasks.exe", args, TimeSpan.FromSeconds(15))).GetAwaiter().GetResult();
        return result.Stdout + Environment.NewLine + result.Stderr;
    }
}

public enum TaskEnablement
{
    Unknown = 0,
    Enabled,
    Disabled,
    Missing
}

public sealed class PowerPlanManager
{
    private readonly IProcessRunner _processes;
    private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public PowerPlanManager(IProcessRunner? processes = null)
    {
        _processes = processes ?? ProcessRunner.Shared;
    }

    public void ActivateUltimatePerformance()
    {
        var existing = FindUltimateSchemeGuid();
        if (existing is null)
        {
            var duplicated = RunPowerCfg($"/duplicatescheme {UltimateGuid}");
            existing = ExtractGuid(duplicated) ?? UltimateGuid;
        }

        RunPowerCfg($"/setactive {existing}");
    }

    public bool IsUltimateActive()
    {
        var active = GetActiveScheme() ?? "";
        if (LabelLooksLikeUltimate(active))
            return true;

        var activeGuid = ExtractGuid(active);
        if (activeGuid is not null && activeGuid.Equals(UltimateGuid, StringComparison.OrdinalIgnoreCase))
            return true;

        var listed = FindUltimateSchemeGuid();
        return listed is not null
               && activeGuid is not null
               && listed.Equals(activeGuid, StringComparison.OrdinalIgnoreCase);
    }

    public static bool LabelLooksLikeUltimate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains(UltimateGuid, StringComparison.OrdinalIgnoreCase);
    }

    private string? FindUltimateSchemeGuid()
    {
        var list = RunPowerCfg("/list");
        string? first = null;
        foreach (var raw in list.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!LabelLooksLikeUltimate(raw))
                continue;
            var guid = ExtractGuid(raw);
            if (guid is null) continue;
            if (raw.Contains('*'))
                return guid;
            first ??= guid;
        }
        return first;
    }

    public void ActivateHighPerformance()
    {
        RunPowerCfg("/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    }

    /// <summary>Activates a scheme from a GUID or a powercfg /getactivescheme output line.</summary>
    public void ActivateScheme(string schemeOrOutput)
    {
        var guid = ExtractGuid(schemeOrOutput);
        if (guid is null)
        {
            ActivateHighPerformance();
            return;
        }
        RunPowerCfg($"/setactive {guid}");
    }

    public string? GetActiveScheme()
    {
        var output = RunPowerCfg("/getactivescheme");
        return output.Trim();
    }

    private static string? ExtractGuid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            text,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value : null;
    }

    private string RunPowerCfg(string args)
    {
        var result = _processes.RunAsync(
            new ProcessRequest("powercfg.exe", args, TimeSpan.FromSeconds(15))).GetAwaiter().GetResult();
        return result.Stdout;
    }
}

public sealed class TempCleanupService
{
    public const int DefaultTimeoutMs = 90_000;
    public const int MaxDepth = 12;

    private static readonly EnumerationOptions EnumOpts = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    public IReadOnlyList<string> DefaultTempPaths()
    {
        return new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };
    }

    public long CleanTempFolders(IProgress<string>? progress = null, CancellationToken cancellationToken = default) =>
        CleanDirectories(DefaultTempPaths(), progress, cancellationToken);

    public long CleanDirectories(
        IEnumerable<string> paths,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeoutMs);
        var ct = timeoutCts.Token;

        var list = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var total = Math.Max(1, list.Count);
        long freed = 0;
        var index = 0;
        foreach (var path in list)
        {
            if (ct.IsCancellationRequested)
                break;
            index++;
            if (!Directory.Exists(path))
                continue;
            if (IsReparsePoint(path))
                continue;

            // "pct|path" so UI can parse percent + detail without a new progress type.
            var pct = (int)Math.Round((index - 1) * 100.0 / total);
            progress?.Report($"{pct}|{path}");
            try
            {
                freed += CleanDirectory(path, depth: 0, progress, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Best-effort: skip folders that throw (locked, permission, long path)
            }

            progress?.Report($"{(int)Math.Round(index * 100.0 / total)}|{path}");
        }

        progress?.Report("100|");
        return freed;
    }

    private static long CleanDirectory(string path, int depth, IProgress<string>? progress, CancellationToken ct)
    {
        long freed = 0;
        if (depth > MaxDepth || ct.IsCancellationRequested)
            return 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", EnumOpts))
            {
                ct.ThrowIfCancellationRequested();
                freed += TryDeleteFile(file);
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", EnumOpts))
            {
                ct.ThrowIfCancellationRequested();
                if (IsReparsePoint(dir))
                    continue;

                freed += CleanDirectory(dir, depth + 1, progress, ct);
                TryDeleteEmptyDirectory(dir);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ignore inaccessible trees
        }

        return freed;
    }

    private static long TryDeleteFile(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (IsReparsePoint(info.Attributes))
                return 0;

            var size = info.Length;
            info.Attributes &= ~FileAttributes.ReadOnly;
            info.Delete();
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static void TryDeleteEmptyDirectory(string dir)
    {
        try
        {
            // Never use recursive Directory.Delete — junctions / locked trees hang or wipe too much.
            Directory.Delete(dir, recursive: false);
        }
        catch
        {
            // Still has files (in use) — leave it
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return IsReparsePoint(attrs);
        }
        catch
        {
            return true; // treat unknown as unsafe to recurse
        }
    }

    private static bool IsReparsePoint(FileAttributes attrs) =>
        (attrs & FileAttributes.ReparsePoint) != 0;
}

public sealed class StartupManager
{
    public IReadOnlyList<StartupEntry> GetStartupEntries()
    {
        var list = new List<StartupEntry>();
        CollectFromRunKey(Microsoft.Win32.Registry.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU", list);
        CollectFromRunKey(Microsoft.Win32.Registry.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM", list);
        return list;
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        var root = entry.Hive == "HKCU"
            ? Microsoft.Win32.Registry.CurrentUser
            : Microsoft.Win32.Registry.LocalMachine;

        if (enabled)
        {
            if (string.IsNullOrEmpty(entry.Command)) return;
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.SetValue(entry.Name, entry.Command);
        }
        else
        {
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            try { key?.DeleteValue(entry.Name, throwOnMissingValue: false); } catch { }
        }
    }

    private static void CollectFromRunKey(Microsoft.Win32.RegistryKey root, string path, string hive, List<StartupEntry> list)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                list.Add(new StartupEntry
                {
                    Name = name,
                    Command = key.GetValue(name)?.ToString() ?? "",
                    Hive = hive,
                    IsEnabled = true
                });
            }
        }
        catch { }
    }
}

public sealed class StartupEntry
{
    public required string Name { get; init; }
    public required string Command { get; set; }
    public required string Hive { get; init; }
    public bool IsEnabled { get; set; }
}

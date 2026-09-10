using System.Diagnostics;

namespace WinCleaner.Core.Services;

public sealed class TaskSchedulerManager
{
    public void SetTaskEnabled(string taskPath, bool enabled)
    {
        var args = enabled
            ? $"/Change /TN \"{taskPath}\" /Enable"
            : $"/Change /TN \"{taskPath}\" /Disable";
        RunSchtasks(args);
    }

    public bool TaskExists(string taskPath)
    {
        var output = RunSchtasks($"/Query /TN \"{taskPath}\" /FO LIST");
        return !LooksMissing(output);
    }

    public bool IsTaskEnabled(string taskPath)
    {
        try
        {
            var output = RunSchtasks($"/Query /TN \"{taskPath}\" /FO LIST /V");
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
        if (LooksMissing(output))
            return TaskEnablement.Missing;

        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!LooksLikeStateLine(line))
                continue;

            var value = line.Contains(':') ? line[(line.IndexOf(':') + 1)..].Trim() : line;
            var parsed = ClassifyStateToken(value);
            if (parsed is TaskEnablement.Enabled or TaskEnablement.Disabled)
                return parsed;
        }

        // Verbose listings can mention both words; prefer Disabled if it appears as a state token.
        var disabledHit = false;
        var enabledHit = false;
        foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var classified = ClassifyStateToken(raw.Trim());
            if (classified == TaskEnablement.Disabled)
                disabledHit = true;
            else if (classified == TaskEnablement.Enabled)
                enabledHit = true;
        }

        if (disabledHit && !enabledHit)
            return TaskEnablement.Disabled;
        if (enabledHit && !disabledHit)
            return TaskEnablement.Enabled;

        return TaskEnablement.Unknown;
    }

    internal static bool LooksMissing(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        ReadOnlySpan<string> needles =
        [
            "ERROR:",
            "cannot find",
            "cannot be found",
            "does not exist",
            "bulunamad",
            "nicht gefunden",
            "wurde nicht gefunden",
            "introuvable",
            "n'existe pas",
            "no se encuentra",
            "no se puede encontrar",
            "не найден",
            "не удается найти",
            "找不到",
            "nu a fost găsit",
            "nu există"
        ];

        foreach (var n in needles)
        {
            if (output.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool LooksLikeStateLine(string line)
    {
        ReadOnlySpan<string> prefixes =
        [
            "Scheduled Task State",
            "Task State",
            "Zamanlanmış Görev Durumu",
            "Zamanlanmis Gorev Durumu",
            "Status der geplanten Aufgabe",
            "Geplanter Task-Status",
            "État de la tâche",
            "Etat de la tache",
            "Estado de la tarea",
            "Estado de la tarea programada",
            "Состояние задачи",
            "计划的任务状态",
            "Starea activității planificate"
        ];

        foreach (var p in prefixes)
        {
            if (line.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Generic "State:" / "Durum:" / "Status:" lines that then hold Enabled/Disabled
        if (line.StartsWith("State:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Durum:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("État:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Etat:", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static TaskEnablement ClassifyStateToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return TaskEnablement.Unknown;

        // Longer / disabled phrases first so "Devre dışı" is not parsed as something else.
        ReadOnlySpan<string> disabled =
        [
            "devre dışı",
            "devre disi",
            "disabled",
            "deaktiviert",
            "désactivé",
            "desactive",
            "deshabilitado",
            "desactivado",
            "отключен",
            "отключена",
            "已禁用",
            "dezactivat"
        ];
        foreach (var d in disabled)
        {
            if (text.Contains(d, StringComparison.OrdinalIgnoreCase))
                return TaskEnablement.Disabled;
        }

        ReadOnlySpan<string> enabled =
        [
            "enabled",
            "etkin",
            "aktiviert",
            "activé",
            "active",
            "habilitado",
            "activado",
            "включен",
            "включена",
            "已启用",
            "activat"
        ];
        foreach (var e in enabled)
        {
            if (text.Contains(e, StringComparison.OrdinalIgnoreCase))
                return TaskEnablement.Enabled;
        }

        return TaskEnablement.Unknown;
    }

    private static string RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("schtasks failed to start");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(15000);
        return stdout + Environment.NewLine + stderr;
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
    private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

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
        if (active.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase))
            return true;
        var guid = ExtractGuid(active);
        return guid is not null && string.Equals(guid, UltimateGuid, StringComparison.OrdinalIgnoreCase);
    }

    private string? FindUltimateSchemeGuid()
    {
        var list = RunPowerCfg("/list");
        string? first = null;
        foreach (var raw in list.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!raw.Contains("Ultimate Performance", StringComparison.OrdinalIgnoreCase)
                && !raw.Contains(UltimateGuid, StringComparison.OrdinalIgnoreCase))
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

    private static string RunPowerCfg(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("powercfg failed");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(15000);
        return output;
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

        long freed = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested)
                break;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                continue;
            if (IsReparsePoint(path))
                continue;

            progress?.Report(path);
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
        }

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

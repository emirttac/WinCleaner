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

    public bool IsTaskEnabled(string taskPath)
    {
        try
        {
            var output = RunSchtasks($"/Query /TN \"{taskPath}\" /FO LIST /V");
            return output.Contains("Enabled", StringComparison.OrdinalIgnoreCase) &&
                   !output.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
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
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(15000);
        return output;
    }
}

public sealed class PowerPlanManager
{
    private const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public void ActivateUltimatePerformance()
    {
        // Duplicate/unlock the hidden Ultimate Performance plan then set active
        RunPowerCfg($"/duplicatescheme {UltimateGuid}");
        RunPowerCfg($"/setactive {UltimateGuid}");
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
    public long CleanTempFolders()
    {
        long freed = 0;
        var paths = new[]
        {
            Path.GetTempPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(path)) continue;
            freed += CleanDirectory(path);
        }
        return freed;
    }

    private static long CleanDirectory(string path)
    {
        long freed = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    var size = info.Length;
                    info.Delete();
                    freed += size;
                }
                catch { }
            }
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var size = DirSize(dir);
                    Directory.Delete(dir, recursive: true);
                    freed += size;
                }
                catch { }
            }
        }
        catch { }
        return freed;
    }

    private static long DirSize(string path)
    {
        long size = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return size;
    }
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

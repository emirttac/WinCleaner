using System.Management;
using Microsoft.Win32;
using WinCleaner.Core.Models;

namespace WinCleaner.Core.Compatibility;

public static class OsCompatibility
{
    public static OsInfo Detect()
    {
        var build = GetBuildNumber();
        var family = build >= 22000 ? OsFamily.Windows11 :
            build >= 10240 ? OsFamily.Windows10 : OsFamily.Unknown;

        var display = family switch
        {
            OsFamily.Windows11 => $"Windows 11 (Build {build})",
            OsFamily.Windows10 => $"Windows 10 (Build {build})",
            _ => $"Windows (Build {build})"
        };

        return new OsInfo
        {
            Family = family,
            DisplayName = display,
            BuildNumber = build,
            VersionString = Environment.OSVersion.Version.ToString()
        };
    }

    public static bool IsCompatible(int? minBuild, bool win11Only, OsInfo os)
    {
        if (!os.IsSupported) return false;
        if (win11Only && os.Family != OsFamily.Windows11) return false;
        if (minBuild.HasValue && os.BuildNumber < minBuild.Value) return false;
        return true;
    }

    private static int GetBuildNumber()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var buildStr = key?.GetValue("CurrentBuildNumber")?.ToString();
            if (int.TryParse(buildStr, out var build))
                return build;
        }
        catch
        {
            // fall through
        }

        return Environment.OSVersion.Version.Build;
    }
}

public static class SystemInfoService
{
    public static SystemHardwareInfo Collect(OsInfo os)
    {
        return new SystemHardwareInfo
        {
            CpuName = QueryWmi("Win32_Processor", "Name") ?? "Unknown CPU",
            TotalRamBytes = QueryRam(),
            GpuName = QueryWmi("Win32_VideoController", "Name") ?? "Unknown GPU",
            OsDisplayName = os.DisplayName,
            OsBuild = os.BuildNumber,
            Disks = QueryDisks()
        };
    }

    private static long QueryRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                return Convert.ToInt64(obj["TotalPhysicalMemory"]);
            }
        }
        catch { }
        return 0;
    }

    private static string? QueryWmi(string className, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {className}");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj[property]?.ToString()?.Trim();
            }
        }
        catch { }
        return null;
    }

    private static IReadOnlyList<DiskInfo> QueryDisks()
    {
        var list = new List<DiskInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                list.Add(new DiskInfo
                {
                    Name = drive.Name,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace
                });
            }
        }
        catch { }
        return list;
    }
}

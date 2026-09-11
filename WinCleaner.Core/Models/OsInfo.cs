namespace WinCleaner.Core.Models;

public sealed class OsInfo
{
    public required OsFamily Family { get; init; }
    public required string DisplayName { get; init; }
    public required int BuildNumber { get; init; }
    public required string VersionString { get; init; }
    public bool IsSupported =>
        Family == OsFamily.Windows11 ||
        (Family == OsFamily.Windows10 && BuildNumber >= 19044);
}

public sealed class SystemHardwareInfo
{
    public string CpuName { get; init; } = "Unknown";
    public long TotalRamBytes { get; init; }
    public string GpuName { get; init; } = "Unknown";
    public string OsDisplayName { get; init; } = "Unknown";
    public int OsBuild { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = Array.Empty<DiskInfo>();
}

public sealed class DiskInfo
{
    public string Name { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
}

namespace WinCleaner.Core.Models;

public enum RiskLevel
{
    Safe = 0,
    Caution = 1,
    Dangerous = 2,
    Blocked = 3
}

public enum ServiceStartModeTarget
{
    Automatic,
    Manual,
    Disabled
}

public enum OsFamily
{
    Windows10,
    Windows11,
    Unknown
}

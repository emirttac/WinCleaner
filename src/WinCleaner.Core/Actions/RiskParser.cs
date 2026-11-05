using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public static class RiskParser
{
    public static RiskLevel Parse(string? risk) =>
        risk?.ToLowerInvariant() switch
        {
            "blocked" => RiskLevel.Blocked,
            "dangerous" => RiskLevel.Dangerous,
            "caution" => RiskLevel.Caution,
            _ => RiskLevel.Safe
        };
}

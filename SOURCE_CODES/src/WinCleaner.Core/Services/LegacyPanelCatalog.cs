namespace WinCleaner.Core.Services;

public sealed record LegacyPanelDef(string Id, string Target, string TitleKey);

/// <summary>WinUtil-style classic Control Panel / MMC launchers.</summary>
public static class LegacyPanelCatalog
{
    public static IReadOnlyList<LegacyPanelDef> All { get; } =
    [
        new("legacy-compmgmt", "compmgmt.msc", "tools.legacy.compmgmt"),
        new("legacy-control", "control.exe", "tools.legacy.control"),
        new("legacy-programs", "appwiz.cpl", "tools.legacy.programs"),
        new("legacy-network", "ncpa.cpl", "tools.legacy.network"),
        new("legacy-power", "powercfg.cpl", "tools.legacy.power"),
        new("legacy-sound", "mmsys.cpl", "tools.legacy.sound"),
        new("legacy-system", "sysdm.cpl", "tools.legacy.system"),
        new("legacy-timedate", "timedate.cpl", "tools.legacy.timedate"),
        new("legacy-firewall", "firewall.cpl", "tools.legacy.firewall"),
        new("legacy-restore", "rstrui.exe", "tools.legacy.restore"),
        new("legacy-mouse", "main.cpl", "tools.legacy.mouse"),
        new("legacy-region", "intl.cpl", "tools.legacy.region"),
        new("legacy-security", "wscui.cpl", "tools.legacy.security")
    ];
}

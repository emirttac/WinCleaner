using WinCleaner.Core.Actions;
using WinCleaner.Core.Compatibility;
using WinCleaner.Core.Models;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class RiskParserTests
{
    [Theory]
    [InlineData("Safe", RiskLevel.Safe)]
    [InlineData("caution", RiskLevel.Caution)]
    [InlineData("Dangerous", RiskLevel.Dangerous)]
    [InlineData("BLOCKED", RiskLevel.Blocked)]
    public void Parse_MapsExpectedValues(string input, RiskLevel expected)
    {
        Assert.Equal(expected, RiskParser.Parse(input));
    }
}

public class CatalogLoaderTests
{
    [Fact]
    public void LoadServices_ReturnsEntries()
    {
        var services = CatalogLoader.LoadServices();
        Assert.NotEmpty(services);
        Assert.Contains(services, s => s.ServiceName == "DiagTrack");
        Assert.Contains(services, s => s.Risk == "Blocked");
    }

    [Fact]
    public void LoadTweaks_ContainsGamingAndTelemetry()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        Assert.Contains(tweaks, t => t.Category == "Gaming");
        Assert.Contains(tweaks, t => t.Category == "Telemetry");
        Assert.Contains(tweaks, t => t.Id == "tweak-game-mode");
    }

    [Fact]
    public void LoadPresets_ContainsGamer()
    {
        var presets = CatalogLoader.LoadPresets();
        var gamer = Assert.Single(presets, p => p.Id == "preset-gamer");
        Assert.NotEmpty(gamer.TweakIds);
    }

    [Fact]
    public void LoadTweaks_ContainsWindows11UiCategory()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        Assert.Contains(tweaks, t => t.Id == "tweak-classic-context-menu" && t.Win11Only);
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-copilot");
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-recall");
        Assert.Contains(tweaks, t => t.Id == "tweak-start-iris");
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-widgets");
        Assert.Contains(tweaks, t => t.Id == "tweak-taskbar-left" && t.Win11Only);
        Assert.Contains(tweaks, t => t.Id == "tweak-hide-taskview" && !t.Win11Only);
        Assert.Contains(tweaks, t => t.Id == "tweak-explorer-thispc" && !t.Win11Only);
        Assert.All(
            tweaks.Where(t => t.Category == "Windows11UI" && t.Win11Only),
            t => Assert.True(t.Win11Only));
    }

    [Fact]
    public void LoadTweaks_ContainsCatalogExpansionEntries()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        Assert.Contains(tweaks, t => t.Id == "tweak-inking-typing" && t.Registries is { Count: 3 });
        Assert.Contains(tweaks, t => t.Id == "tweak-feedback-frequency" && t.Registry!.DeleteValueOnDisable);
        Assert.Contains(tweaks, t => t.Id == "tweak-dark-mode" && t.Registries is { Count: 2 });
        Assert.Contains(tweaks, t => t.Id == "tweak-core-parking" && t.Type == "command");
        Assert.Contains(tweaks, t => t.Id == "tweak-network-throttling" && t.Risk == "Caution");
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-ipv6" && t.RequiresReboot);
    }

    [Fact]
    public void LoadServices_ContainsExpansionEntries()
    {
        var services = CatalogLoader.LoadServices();
        Assert.Contains(services, s => s.Id == "svc-pcasvc" && s.ServiceName == "PcaSvc");
        Assert.Contains(services, s => s.Id == "svc-remoteregistry" && s.RecommendedStart == "Disabled");
        Assert.Contains(services, s => s.Id == "svc-retaildemo" && s.Risk == "Safe");
    }

    [Fact]
    public void LoadPresets_ContainsLaptopAndPrivacyMax()
    {
        var presets = CatalogLoader.LoadPresets();
        var laptop = Assert.Single(presets, p => p.Id == "preset-laptop");
        Assert.Contains("tweak-dark-mode", laptop.TweakIds);
        Assert.Contains("svc-retaildemo", laptop.ServiceIds);

        var privacy = Assert.Single(presets, p => p.Id == "preset-privacy-max");
        Assert.Contains("tweak-inking-typing", privacy.TweakIds);
        Assert.Contains("svc-lfsvc", privacy.ServiceIds);
    }

    [Fact]
    public void LoadTweaks_CopilotAndRecall_HaveMinBuild()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        var copilot = Assert.Single(tweaks, t => t.Id == "tweak-disable-copilot");
        Assert.Equal(22000, copilot.MinBuild);
        Assert.True(copilot.Win11Only);
        Assert.NotNull(copilot.RegistryVariants);
        Assert.True(copilot.RegistryVariants!.Count >= 2);

        var recall = Assert.Single(tweaks, t => t.Id == "tweak-disable-recall");
        Assert.Equal(26100, recall.MinBuild);
        Assert.True(recall.Win11Only);
        Assert.NotNull(recall.Registries);
        Assert.True(recall.Registries!.Count >= 2);
    }

    [Fact]
    public void LoadTweaks_AntiCheatFlags_OnVbsHpetMmcss()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-vbs" && t.RequiresAntiCheatConfirm);
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-hpet" && t.RequiresAntiCheatConfirm);
        Assert.Contains(tweaks, t => t.Id == "tweak-mmcss-gpu" && t.RequiresAntiCheatConfirm);
        Assert.Contains(tweaks, t => t.Id == "tweak-mmcss-priority" && t.RequiresAntiCheatConfirm);
        Assert.Contains(tweaks, t => t.Id == "tweak-mmcss-scheduling" && t.RequiresAntiCheatConfirm);
    }

    [Fact]
    public void LoadTweaks_ContainsGamingHardwareTweaks()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-vbs" && t.RequiresReboot && t.Risk == "Caution");
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-dynamic-tick" && t.Type == "command");
        Assert.Contains(tweaks, t => t.Id == "tweak-disable-hpet" && t.Command!.DetectMode == "absent");
    }

    [Fact]
    public void LoadApps_MarksStoreCritical()
    {
        var apps = CatalogLoader.LoadApps();
        var store = Assert.Single(apps, a => a.PackageName == "Microsoft.WindowsStore");
        Assert.True(store.SystemCritical);
    }
}

public class OsCompatibilityTests
{
    [Fact]
    public void Detect_ReturnsSupportedOs()
    {
        var os = OsCompatibility.Detect();
        Assert.True(os.BuildNumber > 0);
        Assert.False(string.IsNullOrWhiteSpace(os.DisplayName));
    }

    [Fact]
    public void IsCompatible_RespectsMinBuild()
    {
        var win11 = new OsInfo
        {
            Family = OsFamily.Windows11,
            BuildNumber = 22631,
            DisplayName = "Win11",
            VersionString = "10.0"
        };
        Assert.True(OsCompatibility.IsCompatible(22631, win11Only: true, win11));
        Assert.False(OsCompatibility.IsCompatible(26100, win11Only: true, win11));
    }

    [Fact]
    public void IsCompatible_RespectsWin11Only()
    {
        var win10 = new OsInfo
        {
            Family = OsFamily.Windows10,
            BuildNumber = 19045,
            DisplayName = "Win10",
            VersionString = "10.0"
        };
        Assert.False(OsCompatibility.IsCompatible(null, win11Only: true, win10));
        Assert.True(OsCompatibility.IsCompatible(null, win11Only: false, win10));
    }
}

public class ChangeResultTests
{
    [Fact]
    public void Ok_And_Fail_Helpers()
    {
        var ok = ChangeResult.Ok("a", "b", "msg");
        Assert.True(ok.Success);
        Assert.Equal("a", ok.OldValue);
        Assert.Equal("b", ok.NewValue);

        var fail = ChangeResult.Fail("nope");
        Assert.False(fail.Success);
        Assert.Equal("nope", fail.Message);
    }
}

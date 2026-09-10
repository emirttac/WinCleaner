using WinCleaner.Core.Actions;
using WinCleaner.Core.Compatibility;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
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
        Assert.Contains(tweaks, t => t.Id == "tweak-core-parking" && t.Type == "command"
            && t.Command!.DetectMode == "acIndexEquals" && t.Command.DetectValue == "100");
        Assert.Contains(tweaks, t => t.Id == "tweak-nic-power-save" && t.Command!.DetectValue == "WC_NIC_POWER_OFF");
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
        Assert.Equal("command", recall.Type, ignoreCase: true);
        Assert.NotNull(recall.Command);
        // Apply must set both policies and disable the Recall optional feature.
        Assert.Contains("DisableAIDataAnalysis", recall.Command!.Arguments);
        Assert.Contains("AllowRecallEnablement", recall.Command.Arguments);
        Assert.Contains("Disable-WindowsOptionalFeature", recall.Command.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(recall.Command.RevertArguments));
        Assert.True(recall.Command.RequiresAdmin);
        Assert.True(recall.RequiresReboot);
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

    [Fact]
    public void LoadApps_XboxAndTeamsHaveModernAliases()
    {
        var apps = CatalogLoader.LoadApps();

        var xbox = Assert.Single(apps, a => a.Id == "app-xboxapp");
        Assert.Contains("Microsoft.GamingApp", xbox.GetPackageNamePatterns(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.XboxApp", xbox.GetPackageNamePatterns(), StringComparer.OrdinalIgnoreCase);
        Assert.True(xbox.MatchesInstalledName("Microsoft.GamingApp"));
        Assert.True(xbox.MatchesInstalledName("Microsoft.XboxApp"));
        Assert.False(xbox.MatchesInstalledName("Microsoft.XboxGamingOverlay"));
        Assert.False(xbox.MatchesInstalledName("Microsoft.XboxIdentityProvider"));

        var teams = Assert.Single(apps, a => a.Id == "app-teams");
        Assert.Contains("MSTeams", teams.GetPackageNamePatterns(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("MicrosoftTeams", teams.GetPackageNamePatterns(), StringComparer.OrdinalIgnoreCase);
        Assert.True(teams.MatchesInstalledName("MSTeams"));
        Assert.True(teams.MatchesInstalledName("MicrosoftTeams"));

        var overlay = Assert.Single(apps, a => a.Id == "app-xboxgamingoverlay");
        Assert.True(overlay.MatchesInstalledName("Microsoft.XboxGamingOverlay"));
        Assert.True(overlay.MatchesInstalledName("Microsoft.XboxGameOverlay"));
    }

    [Fact]
    public void LoadApps_ContainsWin11InboxBloat()
    {
        var apps = CatalogLoader.LoadApps();
        Assert.Contains(apps, a => a.Id == "app-copilot" && a.PackageName == "Microsoft.Copilot");
        Assert.Contains(apps, a => a.Id == "app-bingsearch" && a.PackageName == "Microsoft.BingSearch");
        Assert.Contains(apps, a => a.Id == "app-outlook" && a.PackageName == "Microsoft.OutlookForWindows");
        Assert.Contains(apps, a => a.Id == "app-devhome" && a.Win11Only);
        Assert.Contains(apps, a => a.Id == "app-xboxtcui" && a.PackageName == "Microsoft.Xbox.TCUI");
        Assert.Contains(apps, a => a.Id == "app-family" && a.PackageName == "MicrosoftCorporationII.MicrosoftFamily");
    }

    [Fact]
    public void LoadTweaks_Win11AndOneShotFixes()
    {
        var tweaks = CatalogLoader.LoadTweaks();

        var copilot = Assert.Single(tweaks, t => t.Id == "tweak-disable-copilot");
        var v22631 = Assert.Single(copilot.RegistryVariants!, v => v.MinBuild == 22631);
        Assert.True(v22631.Registries is { Count: >= 3 });
        Assert.Contains(v22631.Registries!, r => r.Name == "ShowCopilotButton");
        Assert.Contains(v22631.Registries!, r => r.Name == "TurnOffWindowsCopilot");

        var widgets = Assert.Single(tweaks, t => t.Id == "tweak-disable-widgets");
        Assert.True(widgets.Registries is { Count: 2 });
        Assert.Contains(widgets.Registries!, r => r.Name == "TaskbarDa");
        Assert.Contains(widgets.Registries!, r => r.Name == "AllowNewsAndInterests");

        var bing = Assert.Single(tweaks, t => t.Id == "tweak-bing-search");
        Assert.True(bing.Registries is { Count: 2 });
        Assert.Contains(bing.Registries!, r => r.Name == "DisableSearchBoxSuggestions");

        var delivery = Assert.Single(tweaks, t => t.Id == "tweak-delivery-opt");
        Assert.True(delivery.Registries is { Count: 2 });
        Assert.Contains(delivery.Registries!, r => r.Path.Contains("Policies", StringComparison.OrdinalIgnoreCase));

        var tcp = Assert.Single(tweaks, t => t.Id == "tweak-tcp-autotuning");
        Assert.True(string.IsNullOrWhiteSpace(tcp.Command!.RevertArguments));
        Assert.True(tcp.Command.RequiresAdmin);
        Assert.True(tcp.Command.OneShot);

        var dns = Assert.Single(tweaks, t => t.Id == "tweak-dns-flush");
        Assert.True(string.IsNullOrWhiteSpace(dns.Command!.RevertArguments));
        Assert.True(dns.Command.OneShot);

        var mouse = Assert.Single(tweaks, t => t.Id == "tweak-mouse-accel");
        Assert.True(mouse.Registries is { Count: 3 });
        Assert.Contains(mouse.Registries!, r => r.Name == "MouseThreshold1" && r.EnabledValue == "0");
        Assert.Contains(mouse.Registries!, r => r.Name == "MouseThreshold2" && r.EnabledValue == "0");

        var nagle = Assert.Single(tweaks, t => t.Id == "tweak-nagle-ack");
        Assert.True(nagle.Registry!.ApplyToAllSubkeys);
        var nodelay = Assert.Single(tweaks, t => t.Id == "tweak-tcp-nodelay");
        Assert.True(nodelay.Registry!.ApplyToAllSubkeys);
    }

    [Fact]
    public void LoadServices_TabletInputHasWin11Alias()
    {
        var tablet = Assert.Single(CatalogLoader.LoadServices(), s => s.Id == "svc-tabletinput");
        Assert.Equal("TabletInputService", tablet.ServiceName);
        Assert.Contains(tablet.GetServiceNames(), n => n.Equals("TextInputManagementService", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Catalogs_HaveUniqueIds_AndCompleteSpecs_AndPresetRefsResolve()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        var services = CatalogLoader.LoadServices();
        var apps = CatalogLoader.LoadApps();
        var installers = CatalogLoader.LoadInstallerApps();
        var presets = CatalogLoader.LoadPresets();

        Assert.Equal(tweaks.Count, tweaks.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(services.Count, services.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(apps.Count, apps.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(installers.Count, installers.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var t in tweaks)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id));
            switch (t.Type.ToLowerInvariant())
            {
                case "registry":
                    Assert.True(
                        t.Registry is not null
                        || t.Registries is { Count: > 0 }
                        || t.RegistryVariants is { Count: > 0 },
                        $"{t.Id} registry spec missing");
                    break;
                case "command":
                    Assert.False(string.IsNullOrWhiteSpace(t.Command?.FileName), t.Id);
                    Assert.False(string.IsNullOrWhiteSpace(t.Command?.Arguments), t.Id);
                    break;
                case "service":
                    Assert.False(string.IsNullOrWhiteSpace(t.Service?.ServiceName), t.Id);
                    break;
                case "task":
                    Assert.False(string.IsNullOrWhiteSpace(t.Task?.TaskPath), t.Id);
                    break;
                case "powerplan":
                    Assert.NotNull(t.PowerPlan);
                    break;
                default:
                    Assert.True(false, $"{t.Id} has unknown type '{t.Type}'");
                    break;
            }
        }

        var tweakIds = tweaks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serviceIds = services.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            foreach (var id in preset.TweakIds)
                Assert.True(tweakIds.Contains(id), $"preset {preset.Id} missing tweak {id}");
            foreach (var id in preset.ServiceIds)
                Assert.True(serviceIds.Contains(id), $"preset {preset.Id} missing service {id}");
        }
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

public class RegistryManagerValuesEqualTests
{
    [Fact]
    public void ValuesEqual_DwordAcceptsUnsignedNegativeOne()
    {
        Assert.True(RegistryManager.ValuesEqual("-1", "4294967295", "DWord"));
        Assert.True(RegistryManager.ValuesEqual("4294967295", "-1", "DWord"));
        Assert.True(RegistryManager.ValuesEqual("255", "255", "DWord"));
        Assert.False(RegistryManager.ValuesEqual(null, "0", "DWord"));
        Assert.False(RegistryManager.ValuesEqual("1", "0", "DWord"));
    }
}

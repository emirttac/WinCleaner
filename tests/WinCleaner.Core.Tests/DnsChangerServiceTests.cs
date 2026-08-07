using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class DnsChangerServiceTests
{
    [Fact]
    public void Presets_ContainExpectedProviders()
    {
        Assert.Contains(DnsChangerService.Presets, p => p.Kind == DnsPresetKind.Dhcp && p.IsDhcp);
        Assert.Contains(DnsChangerService.Presets, p => p.Primary == "1.1.1.1" && p.Secondary == "1.0.0.1");
        Assert.Contains(DnsChangerService.Presets, p => p.Primary == "8.8.8.8");
        Assert.Contains(DnsChangerService.Presets, p => p.Primary == "9.9.9.9");
    }

    [Fact]
    public void LoadTweaks_ContainsNetbios()
    {
        var tweak = WinCleaner.Data.CatalogLoader.LoadTweaks()
            .Single(t => t.Id == "tweak-disable-netbios");
        Assert.Equal("Network", tweak.Category);
        Assert.True(tweak.Registry!.ApplyToAllSubkeys);
        Assert.Equal("2", tweak.Registry.EnabledValue);
        Assert.Equal("NetbiosOptions", tweak.Registry.Name);
    }
}

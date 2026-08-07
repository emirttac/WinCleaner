using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class InstallerCatalogTests
{
    [Fact]
    public void LoadInstallerApps_ContainsRequiredExamples()
    {
        var apps = CatalogLoader.LoadInstallerApps();
        Assert.NotEmpty(apps);
        Assert.Contains(apps, a => a.WingetId == "Google.Chrome");
        Assert.Contains(apps, a => a.WingetId == "Discord.Discord");
        Assert.Contains(apps, a => a.WingetId == "Valve.Steam");
        Assert.Contains(apps, a => a.WingetId == "7zip.7zip");
        Assert.Contains(apps, a => a.WingetId == "Microsoft.VisualStudioCode");
        Assert.Contains(apps, a => a.WingetId == "VideoLAN.VLC");
        Assert.Contains(apps, a => a.WingetId == "Telegram.TelegramDesktop");
        Assert.Contains(apps, a => a.WingetId == "9NKSQGP7F2NH");
        Assert.Contains(apps, a => a.WingetId == "voidtools.Everything");
        Assert.All(apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.WingetId));
            Assert.False(string.IsNullOrWhiteSpace(a.Category));
            Assert.False(string.IsNullOrWhiteSpace(a.IconGlyph));
        });
    }
}

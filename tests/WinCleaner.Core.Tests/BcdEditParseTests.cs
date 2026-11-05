using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class BcdEditParseTests
{
    [Fact]
    public void ParseBcdValue_FindsDisabledDynamicTick()
    {
        const string sample = """
            Windows Boot Loader
            -------------------
            identifier              {current}
            device                  partition=C:
            path                    \WINDOWS\system32\winload.efi
            disabledynamictick      Yes
            useplatformclock        Yes
            """;

        Assert.Equal("Yes", ElevatedCommandRunner.ParseBcdValue(sample, "disabledynamictick"));
        Assert.Equal("Yes", ElevatedCommandRunner.ParseBcdValue(sample, "useplatformclock"));
        Assert.Null(ElevatedCommandRunner.ParseBcdValue(sample, "missingkey"));
    }

    [Fact]
    public void ParseBcdValue_ReadsLocalizedYesWithoutWordList()
    {
        const string sample = """
            identifier              {current}
            disabledynamictick      Evet
            useplatformclock        Hayır
            """;

        Assert.Equal("Evet", ElevatedCommandRunner.ParseBcdValue(sample, "disabledynamictick"));
        Assert.Equal("Hayır", ElevatedCommandRunner.ParseBcdValue(sample, "useplatformclock"));
        Assert.NotNull(ElevatedCommandRunner.ParseBcdValue(sample, "disabledynamictick"));
    }

    [Fact]
    public void DynamicTickAndHpet_UsePresentDetectMode()
    {
        var tweaks = WinCleaner.Data.CatalogLoader.LoadTweaks();
        var tick = tweaks.Single(t => t.Id == "tweak-disable-dynamic-tick");
        var hpet = tweaks.Single(t => t.Id == "tweak-disable-hpet");
        Assert.Equal("present", tick.Command!.DetectMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("present", hpet.Command!.DetectMode, StringComparer.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(tick.Command.DetectValue));
        Assert.True(string.IsNullOrWhiteSpace(hpet.Command.DetectValue));
    }
}

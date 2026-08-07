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
}

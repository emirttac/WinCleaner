using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class CommandStateProbeTests
{
    [Fact]
    public void ParsePowerCfgAcSettingIndex_ReadsCurrentAc_NotMaximum()
    {
        const string sample = """
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
              Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
                Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor performance core parking min cores)
                  Minimum Possible Setting: 0x00000000
                  Maximum Possible Setting: 0x00000064
                  Possible Settings increment: 0x00000001
                  Possible Settings units: %
                Current AC Power Setting Index: 0x00000064
                Current DC Power Setting Index: 0x00000005
            """;

        Assert.Equal(100, CommandStateProbe.ParsePowerCfgAcSettingIndex(sample));
    }

    [Fact]
    public void ParsePowerCfgAcSettingIndex_IgnoresMaximumOnly()
    {
        const string sample = """
                  Minimum Possible Setting: 0x00000000
                  Maximum Possible Setting: 0x00000064
                Current DC Power Setting Index: 0x00000005
            """;

        Assert.Null(CommandStateProbe.ParsePowerCfgAcSettingIndex(sample));
    }

    [Fact]
    public void ParsePowerCfgAcSettingIndex_GermanAcLine()
    {
        const string sample = "Aktueller AC-Energieeinstellungsindex: 0x00000064";
        Assert.Equal(100, CommandStateProbe.ParsePowerCfgAcSettingIndex(sample));
    }
}

public class TaskSchedulerParseTests
{
    [Fact]
    public void ParseScheduledTaskState_XmlEnabled()
    {
        const string output = """
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Settings>
                <Enabled>true</Enabled>
              </Settings>
            </Task>
            """;

        Assert.Equal(TaskEnablement.Enabled, TaskSchedulerManager.ParseScheduledTaskState(output));
    }

    [Fact]
    public void ParseScheduledTaskState_XmlDisabled()
    {
        const string output = "<Task><Settings><Enabled>false</Enabled></Settings></Task>";
        Assert.Equal(TaskEnablement.Disabled, TaskSchedulerManager.ParseScheduledTaskState(output));
    }

    [Fact]
    public void LooksMissing_AnyLanguageErrorWithoutTaskXml()
    {
        Assert.True(TaskSchedulerManager.LooksMissing("ERROR: The system cannot find the file specified."));
        Assert.True(TaskSchedulerManager.LooksMissing("HATA: Belirtilen görev bulunamadı."));
        Assert.False(TaskSchedulerManager.LooksMissing("<Task><Settings><Enabled>true</Enabled></Settings></Task>"));
    }

    [Fact]
    public void ParseScheduledTaskState_UnknownDoesNotLookDisabled()
    {
        Assert.Equal(TaskEnablement.Unknown, TaskSchedulerManager.ParseScheduledTaskState("<Task><Settings></Settings></Task>"));
    }

    [Fact]
    public void BcdTokens_DoNotTreatTranslatedYesAsEqual()
    {
        Assert.True(CommandStateProbe.TokensEqual("Yes", "yes"));
        Assert.True(CommandStateProbe.TokensEqual("Legacy", "legacy"));
        Assert.False(CommandStateProbe.TokensEqual("Evet", "yes"));
        Assert.False(CommandStateProbe.TokensEqual("Ja", "yes"));
        Assert.False(CommandStateProbe.TokensEqual("hayır", "no"));
    }
}

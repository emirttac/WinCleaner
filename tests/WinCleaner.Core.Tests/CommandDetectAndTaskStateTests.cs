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
    public void ParseScheduledTaskState_EnglishEnabled()
    {
        const string output = """
            Folder: \Microsoft\Windows\Customer Experience Improvement Program
            HostName: PC
            TaskName: \Microsoft\Windows\Customer Experience Improvement Program\Consolidator
            Scheduled Task State: Enabled
            """;

        Assert.Equal(TaskEnablement.Enabled, TaskSchedulerManager.ParseScheduledTaskState(output));
    }

    [Fact]
    public void ParseScheduledTaskState_TurkishDisabled()
    {
        const string output = """
            Klasör: \Microsoft\Windows\Customer Experience Improvement Program
            Zamanlanmış Görev Durumu: Devre dışı
            """;

        Assert.Equal(TaskEnablement.Disabled, TaskSchedulerManager.ParseScheduledTaskState(output));
    }

    [Fact]
    public void ParseScheduledTaskState_TurkishEnabled()
    {
        const string output = "Zamanlanmış Görev Durumu: Etkin";
        Assert.Equal(TaskEnablement.Enabled, TaskSchedulerManager.ParseScheduledTaskState(output));
    }

    [Fact]
    public void LooksMissing_TurkishAndEnglish()
    {
        Assert.True(TaskSchedulerManager.LooksMissing("ERROR: The system cannot find the file specified."));
        Assert.True(TaskSchedulerManager.LooksMissing("HATA: Belirtilen görev bulunamadı."));
        Assert.False(TaskSchedulerManager.LooksMissing("Scheduled Task State: Enabled"));
    }

    [Fact]
    public void ParseScheduledTaskState_UnknownDoesNotLookDisabled()
    {
        Assert.Equal(TaskEnablement.Unknown, TaskSchedulerManager.ParseScheduledTaskState("HostName: PC\nTaskName: Foo"));
    }
}

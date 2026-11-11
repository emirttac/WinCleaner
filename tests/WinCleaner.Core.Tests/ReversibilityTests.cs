using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

[Collection("restore-points")]
public class ReversibilityTests
{
    [Fact]
    public void EveryTweak_HasAKnownReversibility()
    {
        var errors = CatalogLoader.LoadTweaks().SelectMany(ReversibilityRules.Validate).ToList();
        Assert.True(errors.Count == 0, string.Join("; ", errors));
    }

    [Fact]
    public void ExactCommandRevert_DoesNotUseCatalogDefaults()
    {
        Assert.True(CommandRevertBuilder.TryBuild("tweak-core-parking", "40", out _, out var parking));
        Assert.Contains("CPMINCORES 40", parking, StringComparison.Ordinal);
        Assert.DoesNotContain("CPMINCORES 5", parking, StringComparison.Ordinal);

        Assert.True(CommandRevertBuilder.TryBuild("tweak-disable-ipv6", "32", out _, out var ipv6));
        Assert.Contains("-Value 32", ipv6, StringComparison.Ordinal);
        Assert.DoesNotContain("-Value 0", ipv6, StringComparison.Ordinal);

        Assert.True(CommandRevertBuilder.TryBuild("tweak-disable-ipv6", "absent", out _, out var missing));
        Assert.Contains("Remove-ItemProperty", missing, StringComparison.Ordinal);

        Assert.True(CommandRevertBuilder.TryBuild("tweak-explorer-folder-discovery", @"reg:C:\temp\bags.reg", out var file, out var bags));
        Assert.Equal("reg.exe", file);
        Assert.Contains("import", bags, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item", bags, StringComparison.Ordinal);
        Assert.False(CommandRevertBuilder.TryBuild("tweak-explorer-folder-discovery", "export-failed", out _, out _));
        Assert.True(CommandRevertBuilder.NeedsIrreversibleConfirm("tweak-explorer-folder-discovery", "export-failed", false));
        Assert.False(CommandRevertBuilder.NeedsIrreversibleConfirm("tweak-explorer-folder-discovery", "export-failed", true));
    }

    [Fact]
    public void RestorePoint_CountsOnlyANewWinCleanerLabel()
    {
        RestorePointManager.ResetForTests();
        var lists = new Queue<IReadOnlyList<RestorePointRow>>();
        lists.Enqueue([new RestorePointRow(1, "Other app")]);
        lists.Enqueue([new RestorePointRow(1, "Other app"), new RestorePointRow(2, "Other app")]);
        RestorePointManager.ListOverride = () => lists.Dequeue();
        RestorePointManager.CreateCoreOverride = _ => true;
        try
        {
            var outcome = new RestorePointManager().CreateChecked("WinCleaner");
            Assert.Equal(RestorePointOutcome.NotCreated, outcome);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public void RestorePoint_CreatedWhenTheNewRowHasTheWinCleanerLabel()
    {
        RestorePointManager.ResetForTests();
        var lists = new Queue<IReadOnlyList<RestorePointRow>>();
        lists.Enqueue([new RestorePointRow(1, "WinCleaner")]);
        lists.Enqueue([new RestorePointRow(1, "WinCleaner"), new RestorePointRow(2, "WinCleaner")]);
        RestorePointManager.ListOverride = () => lists.Dequeue();
        RestorePointManager.CreateCoreOverride = _ => true;
        try
        {
            var outcome = new RestorePointManager().CreateChecked("WinCleaner");
            Assert.Equal(RestorePointOutcome.Created, outcome);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public void RestorePoint_ExistingWinCleanerLabel_IsNotAFailure()
    {
        RestorePointManager.ResetForTests();
        var row = new RestorePointRow(4, "WinCleaner");
        RestorePointManager.ListOverride = () => [row];
        RestorePointManager.CreateCoreOverride = _ => true;
        try
        {
            var manager = new RestorePointManager();
            var outcome = manager.CreateChecked("WinCleaner");
            Assert.Equal(RestorePointOutcome.AlreadyExists, outcome);
            Assert.True(manager.HasSessionCheckpoint);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public void RestorePoint_ProtectionOff_IsASeparateOutcome()
    {
        RestorePointManager.ResetForTests();
        var listed = 0;
        RestorePointManager.ProtectionOverride = () => false;
        RestorePointManager.ListOverride = () =>
        {
            listed++;
            return [];
        };
        try
        {
            var outcome = new RestorePointManager().CreateChecked("WinCleaner");
            Assert.Equal(RestorePointOutcome.ProtectionDisabled, outcome);
            Assert.Equal(0, listed);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public void CorruptJournal_IsRenamedAndDoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), "wincleaner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var journal = Path.Combine(root, "undo_history.json");
        File.WriteAllText(journal, "{not json");
        try
        {
            var log = new ChangeLogService(root, Path.Combine(root, "logs"));
            Assert.Empty(log.GetRevertableEntries());
            Assert.True(File.Exists(journal + ".bad"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}

[CollectionDefinition("restore-points", DisableParallelization = true)]
public sealed class RestorePointCollection;


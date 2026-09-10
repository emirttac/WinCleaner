using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class PrerequisiteCheckerTests
{
    [Fact]
    public void GetOsBuild_IsPositiveOnWindows()
    {
        Assert.True(PrerequisiteChecker.GetOsBuild() > 0);
    }

    [Fact]
    public void Check_DoesNotThrow()
    {
        var result = PrerequisiteChecker.Check(includeDotNetIfFrameworkDependent: false);
        Assert.NotNull(result.Issues);
    }
}

public class ClassicContextMenuRegistryTests
{
    [Fact]
    public void EnableAndDetect_RoundTrips_WhenWritable()
    {
        var reg = new RegistryManager();
        var before = reg.IsClassicContextMenuEnabled();
        try
        {
            reg.EnableClassicContextMenu();
            Assert.True(reg.IsClassicContextMenuEnabled());
        }
        finally
        {
            // Restore prior state
            reg.DeleteKeyTree("HKCU", @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}");
            if (before)
            {
                try { reg.EnableClassicContextMenu(); } catch { /* ignore in CI */ }
            }
        }
    }
}

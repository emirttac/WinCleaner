using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class SystemHealthServiceTests
{
    [Fact]
    public void CreateGodModeFolder_CreatesDesktopDirectory()
    {
        var tempDesktop = Path.Combine(Path.GetTempPath(), "WinCleanerGodModeTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDesktop);
        try
        {
            var svc = new SystemHealthService();
            var created = svc.CreateGodModeFolder(tempDesktop);
            Assert.True(Directory.Exists(created));
            Assert.Contains("ED7BA470-8E54-465E-825C-99712043E01C", created, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDesktop, recursive: true); } catch { }
        }
    }

    [Fact]
    public void TakeOwnershipMenu_RoundTrip_EnableDisable()
    {
        if (!PrivilegeHelper.IsAdministrator())
        {
            // HKCR writes need elevation in CI / non-admin runs
            return;
        }

        var svc = new SystemHealthService();
        var before = svc.IsTakeOwnershipMenuEnabled();
        try
        {
            svc.SetTakeOwnershipMenu(true, "Take Ownership Test");
            Assert.True(svc.IsTakeOwnershipMenuEnabled());
            svc.SetTakeOwnershipMenu(false, "Take Ownership Test");
            Assert.False(svc.IsTakeOwnershipMenuEnabled());
        }
        finally
        {
            // Restore prior state
            svc.SetTakeOwnershipMenu(before, "Take Ownership");
        }
    }
}

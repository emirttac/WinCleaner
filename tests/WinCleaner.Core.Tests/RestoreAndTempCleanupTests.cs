using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class RestoreAndTempCleanupTests
{
    [Fact]
    public async Task EnsureSession_CreatesOnlyOnce_EvenWhenCalledRepeatedly()
    {
        RestorePointManager.ResetForTests();
        RestorePointManager.MinInterval = TimeSpan.Zero;
        var creates = 0;
        RestorePointManager.CreateCoreOverride = _ =>
        {
            Interlocked.Increment(ref creates);
            return true;
        };

        try
        {
            var mgr = new RestorePointManager();
            var warnings = 0;
            mgr.NotifyMissingSessionCheckpoint = () =>
            {
                Interlocked.Increment(ref warnings);
                return Task.CompletedTask;
            };

            await mgr.EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: true);
            await mgr.EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: true);
            await mgr.EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: true);

            Assert.Equal(1, creates);
            Assert.Equal(1, warnings);
            Assert.True(mgr.HasSessionCheckpoint);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public async Task EnsureSession_SkippedWhenDisabled()
    {
        RestorePointManager.ResetForTests();
        var creates = 0;
        RestorePointManager.CreateCoreOverride = _ =>
        {
            Interlocked.Increment(ref creates);
            return true;
        };

        try
        {
            var mgr = new RestorePointManager();
            await mgr.EnsureSessionCheckpointAsync(enabled: false, warnIfMissing: true);
            Assert.Equal(0, creates);
            Assert.False(mgr.HasSessionCheckpoint);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public async Task EnsureSession_DoesNotRetryAfterFailedCreate()
    {
        RestorePointManager.ResetForTests();
        RestorePointManager.MinInterval = TimeSpan.Zero;
        var creates = 0;
        RestorePointManager.CreateCoreOverride = _ =>
        {
            Interlocked.Increment(ref creates);
            return false;
        };

        try
        {
            var mgr = new RestorePointManager();
            await mgr.EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: false);
            await mgr.EnsureSessionCheckpointAsync(enabled: true, warnIfMissing: false);
            Assert.Equal(1, creates);
            Assert.False(mgr.HasSessionCheckpoint);
        }
        finally
        {
            RestorePointManager.ResetForTests();
        }
    }

    [Fact]
    public void TempCleanup_DeletesFilesAndEmptyFolders_SkipsLockedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinCleanerTempTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var nested = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(nested);
        var freeFile = Path.Combine(nested, "free.txt");
        File.WriteAllText(freeFile, "hello");
        var locked = Path.Combine(root, "locked.dat");
        File.WriteAllBytes(locked, new byte[32]);

        try
        {
            using var fs = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
            var svc = new TempCleanupService();
            var freed = svc.CleanDirectories([root]);

            Assert.True(freed >= 5);
            Assert.False(File.Exists(freeFile));
            Assert.True(File.Exists(locked));
            Assert.False(Directory.Exists(nested));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

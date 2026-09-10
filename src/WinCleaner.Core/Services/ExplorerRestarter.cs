using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinCleaner.Core.Services;

/// <summary>
/// Restarts Windows Explorer so shell UI (context menu, taskbar widgets, etc.) reloads.
/// </summary>
public static class ExplorerRestarter
{
    private const uint ShcneAssocchanged = 0x08000000;
    private const uint ShcnfIdlist = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void Restart()
    {
        try
        {
            // Ask the shell tray to exit cleanly first (avoids elevated-zombie explorers).
            var tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
                PostMessage(tray, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        }
        catch { /* best-effort */ }

        Thread.Sleep(600);

        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(8000);
                }
            }
            catch
            {
                // Best-effort — shell may already be exiting.
            }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }

        Thread.Sleep(500);

        var explorer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");

        Process.Start(new ProcessStartInfo
        {
            FileName = explorer,
            UseShellExecute = true
        });

        try { SHChangeNotify(ShcneAssocchanged, ShcnfIdlist, IntPtr.Zero, IntPtr.Zero); }
        catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}

using System.Diagnostics;

namespace WinCleaner.Core.Services;

/// <summary>
/// Restarts Windows Explorer so shell UI (context menu, taskbar widgets, etc.) reloads.
/// </summary>
public static class ExplorerRestarter
{
    public static void Restart()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(8000);
            }
            catch
            {
                // Best-effort — shell may already be exiting.
            }
        }

        // Give the shell a brief moment before relaunching.
        Thread.Sleep(400);

        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe"),
            UseShellExecute = true
        });
    }
}

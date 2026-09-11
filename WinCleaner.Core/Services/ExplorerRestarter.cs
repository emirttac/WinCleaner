using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinCleaner.Core.Services;

/// <summary>
/// Refreshes or restarts the Windows shell after UI-related registry changes.
/// Avoids WM_CLOSE on Shell_TrayWnd (triggers "Shut Down Windows" on modern builds)
/// and avoids starting a second explorer.exe when the shell already auto-restarted
/// (that opens a File Explorer folder window — especially when WinCleaner is elevated).
/// </summary>
public static class ExplorerRestarter
{
    private const uint ShcneAssocchanged = 0x08000000;
    private const uint ShcnfIdlist = 0x0000;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private static readonly IntPtr HwndBroadcast = (IntPtr)0xFFFF;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    /// <summary>
    /// Soft refresh — same idea as WinUtil <c>Invoke-WinUtilExplorerUpdate -action refresh</c>.
    /// Prefer this for theme / taskbar / Explorer view flags.
    /// </summary>
    public static void Refresh()
    {
        try
        {
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "ImmersiveColorSet",
                SmtoAbortIfHung,
                1000,
                out _);
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "Policy",
                SmtoAbortIfHung,
                1000,
                out _);
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "Environment",
                SmtoAbortIfHung,
                1000,
                out _);
        }
        catch
        {
            // best-effort
        }

        try { SHChangeNotify(ShcneAssocchanged, ShcnfIdlist, IntPtr.Zero, IntPtr.Zero); }
        catch { }
    }

    /// <summary>
    /// Hard shell restart — WinUtil style: <c>taskkill /F /IM explorer.exe</c> then start only if needed.
    /// No WM_CLOSE (that opens the Shut Down Windows dialog on Win10/11).
    /// </summary>
    public static void Restart()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
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

        // Give the user session a moment to auto-respawn the shell.
        Thread.Sleep(1000);

        if (Process.GetProcessesByName("explorer").Length == 0)
        {
            var explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");

            Process.Start(new ProcessStartInfo
            {
                FileName = explorer,
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            });

            // Wait briefly for the shell; do not launch a second instance.
            for (var i = 0; i < 20 && Process.GetProcessesByName("explorer").Length == 0; i++)
                Thread.Sleep(150);
        }

        Refresh();
    }
}

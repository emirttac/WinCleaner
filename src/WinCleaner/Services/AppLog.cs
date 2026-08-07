using System.IO;
using System.Text;

namespace WinCleaner.Services;

/// <summary>
/// Simple file logger for diagnosing freezes/crashes. Writes under %LocalAppData%\WinCleaner\logs.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static string LogDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCleaner", "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string LogFilePath
    {
        get
        {
            if (_logPath is not null) return _logPath;
            _logPath = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
            return _logPath;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        if (ex is not null)
        {
            sb.AppendLine();
            sb.Append(ex);
        }
        Write("ERROR", sb.ToString());
    }

    public static void Crash(string source, Exception ex)
    {
        var crashFile = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var body =
            $"[{DateTime.Now:O}] CRASH ({source}){Environment.NewLine}{ex}{Environment.NewLine}";
        try
        {
            File.AppendAllText(crashFile, body);
            Write("CRASH", $"{source}: {ex.GetType().Name}: {ex.Message}");
        }
        catch
        {
            // last resort — ignore logging failures
        }
    }

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogFilePath, line);
            }
            catch
            {
                // ignore
            }
        }
#if DEBUG
        System.Diagnostics.Debug.WriteLine(line.TrimEnd());
#endif
    }
}

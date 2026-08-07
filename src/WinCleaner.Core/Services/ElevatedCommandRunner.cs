using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace WinCleaner.Core.Services;

public static class PrivilegeHelper
{
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}

public sealed record ProcessCommandResult(bool Success, int ExitCode, string StdOut, string StdErr, string? ErrorMessage)
{
    public static ProcessCommandResult Fail(string message, int exitCode = -1, string stdout = "", string stderr = "") =>
        new(false, exitCode, stdout, stderr, message);

    public static ProcessCommandResult Ok(int exitCode, string stdout, string stderr) =>
        new(true, exitCode, stdout, stderr, null);
}

/// <summary>
/// Runs elevated system tools (bcdedit, etc.) with captured output and friendly errors.
/// </summary>
public static class ElevatedCommandRunner
{
    public static ProcessCommandResult Run(string fileName, string arguments, int timeoutMs = 30000)
    {
        try
        {
            var exe = ResolveExecutable(fileName);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return ProcessCommandResult.Fail($"Failed to start '{exe}'.");

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return ProcessCommandResult.Fail($"'{exe}' timed out after {timeoutMs} ms.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (proc.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                detail = detail.Trim();
                if (IsBenignBcdDeleteMiss(fileName, arguments, detail))
                    return ProcessCommandResult.Ok(proc.ExitCode, stdout, stderr);

                var adminHint = PrivilegeHelper.IsAdministrator()
                    ? ""
                    : " Run WinCleaner as Administrator.";
                return ProcessCommandResult.Fail(
                    string.IsNullOrWhiteSpace(detail)
                        ? $"Command failed (exit {proc.ExitCode}).{adminHint}"
                        : $"{detail}{adminHint}",
                    proc.ExitCode,
                    stdout,
                    stderr);
            }

            return ProcessCommandResult.Ok(proc.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            var adminHint = PrivilegeHelper.IsAdministrator()
                ? ""
                : " Administrator privileges are required.";
            return ProcessCommandResult.Fail($"{ex.Message}{adminHint}");
        }
    }

    public static string? ReadBcdValue(string key)
    {
        var result = Run("bcdedit", "/enum {current}");
        if (!result.Success && string.IsNullOrWhiteSpace(result.StdOut))
            return null;

        return ParseBcdValue(result.StdOut, key);
    }

    public static bool BcdKeyPresent(string key) => ReadBcdValue(key) is not null;

    internal static string? ParseBcdValue(string enumOutput, string key)
    {
        if (string.IsNullOrWhiteSpace(enumOutput) || string.IsNullOrWhiteSpace(key))
            return null;

        foreach (var rawLine in enumOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // bcdedit lines: "identifier               {current}" or "disabledynamictick      Yes"
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (!parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return parts.Length > 1 ? parts[1].Trim() : "";
        }

        return null;
    }

    private static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName))
            return fileName;

        var leaf = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".exe";
        if (leaf.Equals("bcdedit.exe", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

        var systemPath = Path.Combine(Environment.SystemDirectory, leaf);
        if (File.Exists(systemPath))
            return systemPath;

        return fileName;
    }

    /// <summary>
    /// bcdedit /deletevalue returns non-zero when the element is already missing — treat as success.
    /// </summary>
    private static bool IsBenignBcdDeleteMiss(string fileName, string arguments, string detail)
    {
        if (!fileName.Contains("bcdedit", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!arguments.Contains("/deletevalue", StringComparison.OrdinalIgnoreCase))
            return false;

        // EN + TR-ish phrases
        return detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("was not found", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("element data", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("bulunamad", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("mevcut değil", StringComparison.OrdinalIgnoreCase);
    }
}

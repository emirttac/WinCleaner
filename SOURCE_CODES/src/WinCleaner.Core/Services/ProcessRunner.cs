using System.Diagnostics;
using System.Text;
using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Compatibility;

namespace WinCleaner.Core.Services;

public sealed class ProcessRunner : IProcessRunner
{
    static ProcessRunner()
    {
        // OEM code pages such as 437 are not in the default .NET encoding set.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IProcessRunner Shared { get; } = new ProcessRunner();

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start '{request.FileName}'.");

        var stdoutTask = ReadPipeAsync(process.StandardOutput.BaseStream, request.LineProgress);
        var stderrTask = ReadPipeAsync(process.StandardError.BaseStream, request.LineProgress);

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            var drained = await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            var cancelled = cancellationToken.IsCancellationRequested;
            return new ProcessResult(null, drained.Stdout, drained.Stderr, TimedOut: !cancelled, Cancelled: cancelled);
        }

        var text = await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
        if (!process.HasExited)
            return new ProcessResult(null, text.Stdout, text.Stderr, TimedOut: false, Cancelled: false);

        return new ProcessResult(process.ExitCode, text.Stdout, text.Stderr, TimedOut: false, Cancelled: false);
    }

    private static async Task<string> ReadPipeAsync(Stream stream, IProgress<string>? progress)
    {
        using var all = new MemoryStream();
        using var line = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            }
            catch (IOException)
            {
                break;
            }

            if (read == 0)
                break;

            all.Write(buffer, 0, read);
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b == (byte)'\n')
                {
                    ReportLine(line, progress);
                    line.SetLength(0);
                }
                else
                {
                    line.WriteByte(b);
                }
            }
        }

        if (line.Length > 0)
            ReportLine(line, progress);

        return ConsoleOutput.Decode(all.ToArray());
    }

    private static void ReportLine(MemoryStream line, IProgress<string>? progress)
    {
        var raw = line.ToArray();
        if (raw.Length > 0 && raw[^1] == (byte)'\r')
            raw = raw[..^1];
        progress?.Report(ConsoleOutput.Decode(raw));
    }

    private static async Task<(string Stdout, string Stderr)> DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch
        {
            // A killed process closes the pipes. Captured text is still in the builders.
        }

        var stdoutText = stdout.IsCompletedSuccessfully ? stdout.Result : "";
        var stderrText = stderr.IsCompletedSuccessfully ? stderr.Result : "";
        return (stdoutText, stderrText);
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }

        try
        {
            process.WaitForExit(2000);
        }
        catch
        {
            // Exit code stays unread when the process has not exited.
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class MachineSystemInfo : ISystemInfo
{
    public int Build => OsCompatibility.Detect().BuildNumber;
    public string Sku => "Unknown";
    public System.Runtime.InteropServices.Architecture OsArch =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
}

public sealed class OsFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
}

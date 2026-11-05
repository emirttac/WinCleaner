using System.Diagnostics;
using System.Globalization;
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
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            throw new InvalidOperationException($"Failed to start '{request.FileName}'.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = ReadLinesAsync(process.StandardOutput, stdout, request.LineProgress);
        var stderrTask = ReadLinesAsync(process.StandardError, stderr, request.LineProgress);

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            var cancelled = cancellationToken.IsCancellationRequested;
            return new ProcessResult(null, stdout.ToString(), stderr.ToString(), TimedOut: !cancelled, Cancelled: cancelled);
        }

        await DrainAsync(stdoutTask, stderrTask).ConfigureAwait(false);
        if (!process.HasExited)
            return new ProcessResult(null, stdout.ToString(), stderr.ToString(), TimedOut: false, Cancelled: false);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false, Cancelled: false);
    }

    private static async Task ReadLinesAsync(StreamReader reader, StringBuilder buffer, IProgress<string>? progress)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
                return;
            buffer.AppendLine(line);
            progress?.Report(line);
        }
    }

    private static async Task DrainAsync(Task stdout, Task stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch
        {
            // A killed process closes the pipes. Captured text is still in the builders.
        }
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

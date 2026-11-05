using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinCleaner.Core.Models;

namespace WinCleaner.Core.Abstractions;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

public sealed record ProcessRequest(string FileName, string Arguments, TimeSpan Timeout)
{
    public IProgress<string>? LineProgress { get; init; }
}

public sealed record ProcessResult(int? ExitCode, string Stdout, string Stderr, bool TimedOut, bool Cancelled);

public interface IRegistryAccess
{
    string? GetValueAsString(string hive, string path, string name);
    void SetValue(string hive, string path, string name, object value, RegistryValueKind kind);
    void DeleteValue(string hive, string path, string name);
}

public interface IServiceControl
{
    void SetStartType(string serviceName, ServiceStartModeTarget target);
}

public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string contents);
}

public interface ISystemInfo
{
    int Build { get; }
    string Sku { get; }
    Architecture OsArch { get; }
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

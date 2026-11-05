using WinCleaner.Core.Abstractions;

namespace WinCleaner.Core.Services;

public sealed class RegistryTransfer
{
    private readonly IProcessRunner _processes;

    public RegistryTransfer(IProcessRunner processes) => _processes = processes;

    public Task<ProcessResult> ExportAsync(string key, string file, CancellationToken cancellationToken = default) =>
        _processes.RunAsync(new ProcessRequest("reg.exe", $"export \"{key}\" \"{file}\" /y", TimeSpan.FromSeconds(60)), cancellationToken);

    public Task<ProcessResult> ImportAsync(string file, CancellationToken cancellationToken = default) =>
        _processes.RunAsync(new ProcessRequest("reg.exe", $"import \"{file}\"", TimeSpan.FromSeconds(60)), cancellationToken);
}

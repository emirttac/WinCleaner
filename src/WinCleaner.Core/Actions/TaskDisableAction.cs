using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class TaskDisableAction : IChangeAction
{
    private readonly TaskSchedulerManager _tasks;
    private readonly string _taskPath;
    private bool _wasEnabled = true;

    public TaskDisableAction(string id, string displayName, string description, string taskPath, TaskSchedulerManager tasks)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = "Telemetry";
        Risk = RiskLevel.Safe;
        _taskPath = taskPath;
        _tasks = tasks;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_tasks.IsTaskEnabled(_taskPath) ? "Enabled" : "Disabled");

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        _wasEnabled = _tasks.IsTaskEnabled(_taskPath);
        if (!_tasks.TaskExists(_taskPath))
            return Task.FromResult(ChangeResult.Ok(_wasEnabled ? "Enabled" : "Disabled", "Disabled", "Task not present"));
        _tasks.SetTaskEnabled(_taskPath, enabled: false);
        return Task.FromResult(ChangeResult.Ok(_wasEnabled ? "Enabled" : "Disabled", "Disabled"));
    }

    public bool IsApplied()
    {
        if (!_tasks.TaskExists(_taskPath))
            return true;
        return !_tasks.IsTaskEnabled(_taskPath);
    }

    public ActionPreview BuildPreview()
    {
        var exists = _tasks.TaskExists(_taskPath);
        var current = !exists ? "Missing" : (_tasks.IsTaskEnabled(_taskPath) ? "Enabled" : "Disabled");
        const string proposed = "Disabled";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = $"task:{_taskPath}",
                    Current = current,
                    Proposed = proposed,
                    Kind = PreviewDiffKind.Set,
                    Unchanged = IsApplied()
                }
            ]);
    }

    public void SeedPreviousState(string? oldValue)
    {
        if (string.Equals(oldValue, "Enabled", StringComparison.OrdinalIgnoreCase))
            _wasEnabled = true;
        else if (string.Equals(oldValue, "Disabled", StringComparison.OrdinalIgnoreCase))
            _wasEnabled = false;
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        _tasks.SetTaskEnabled(_taskPath, _wasEnabled);
        return Task.FromResult(ChangeResult.Ok("Disabled", _wasEnabled ? "Enabled" : "Disabled"));
    }
}

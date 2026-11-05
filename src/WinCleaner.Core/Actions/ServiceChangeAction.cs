using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class ServiceChangeAction : IChangeAction
{
    private readonly ServiceManager _services;
    private readonly string _serviceName;
    private readonly ServiceStartModeTarget _desired;
    private ServiceStartMode? _previousStart;
    private bool _wasRunning;

    public ServiceChangeAction(
        string id,
        string displayName,
        string description,
        string category,
        RiskLevel risk,
        string serviceName,
        ServiceStartModeTarget desired,
        ServiceManager services)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Category = category;
        Risk = risk;
        _serviceName = serviceName;
        _desired = desired;
        _services = services;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        var info = _services.GetService(_serviceName);
        if (info is null) return Task.FromResult<string?>("NotInstalled");
        return Task.FromResult<string?>($"{info.StartType}/{info.Status}");
    }

    public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var info = _services.GetService(_serviceName);
        if (info is null)
            return Task.FromResult(ChangeResult.Fail($"Service {_serviceName} not found."));

        _previousStart = info.StartType;
        _wasRunning = info.Status == ServiceControllerStatus.Running;
        var oldValue = $"{info.StartType}/{info.Status}";

        if (_desired == ServiceStartModeTarget.Disabled && _wasRunning)
            _services.Stop(_serviceName);

        _services.SetStartType(_serviceName, _desired);

        if (_desired == ServiceStartModeTarget.Automatic)
        {
            try { _services.Start(_serviceName); } catch { /* may already be running */ }
        }

        var newInfo = _services.GetService(_serviceName);
        return Task.FromResult(ChangeResult.Ok(oldValue, $"{_desired}/{newInfo?.Status}", "Service updated"));
    }

    public bool IsApplied()
    {
        var info = _services.GetService(_serviceName);
        if (info is null) return false;
        return _desired switch
        {
            ServiceStartModeTarget.Disabled => info.StartType == ServiceStartMode.Disabled,
            ServiceStartModeTarget.Automatic => info.StartType == ServiceStartMode.Automatic,
            _ => info.StartType == ServiceStartMode.Manual
        };
    }

    public ActionPreview BuildPreview()
    {
        var info = _services.GetService(_serviceName);
        var current = info is null ? "NotInstalled" : info.StartType.ToString();
        var proposed = _desired.ToString();
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            proposed,
            PreviewDiffKind.Set,
            [
                new PreviewDiffRow
                {
                    Target = $"service:{_serviceName}",
                    Current = current,
                    Proposed = proposed,
                    Kind = PreviewDiffKind.Set,
                    Unchanged = IsApplied()
                }
            ]);
    }

    public void SeedPreviousState(string? oldValue)
    {
        if (string.IsNullOrWhiteSpace(oldValue))
            return;

        // Format from ApplyAsync: "{StartType}/{Status}"
        var parts = oldValue.Split('/', 2);
        if (parts.Length == 0)
            return;

        _previousStart = ParseStartMode(parts[0]);

        if (parts.Length > 1)
        {
            _wasRunning = parts[1].Contains("Running", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static ServiceStartMode? ParseStartMode(string raw)
    {
        if (Enum.TryParse<ServiceStartMode>(raw, ignoreCase: true, out var start))
            return start;
        if (raw.Contains("Automatic", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Auto", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Automatic;
        if (raw.Contains("Disabled", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Disabled;
        if (raw.Contains("Manual", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Demand", StringComparison.OrdinalIgnoreCase))
            return ServiceStartMode.Manual;
        return null;
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        if (_previousStart is null)
            return Task.FromResult(ChangeResult.Fail("No previous value in change journal."));

        var target = _previousStart.Value switch
        {
            ServiceStartMode.Automatic => ServiceStartModeTarget.Automatic,
            ServiceStartMode.Disabled => ServiceStartModeTarget.Disabled,
            _ => ServiceStartModeTarget.Manual
        };

        _services.SetStartType(_serviceName, target);
        if (_wasRunning && target != ServiceStartModeTarget.Disabled)
        {
            try { _services.Start(_serviceName); } catch { }
        }
        else if (!_wasRunning)
        {
            try { _services.Stop(_serviceName); } catch { }
        }

        return Task.FromResult(ChangeResult.Ok(null, _previousStart.ToString(), "Service reverted"));
    }
}

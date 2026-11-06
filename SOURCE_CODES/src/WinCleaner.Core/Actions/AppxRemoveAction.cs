using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class AppxRemoveAction : IChangeAction
{
    private readonly AppxManager _appx;
    private readonly IReadOnlyList<string> _packageNames;

    public AppxRemoveAction(string id, string displayName, string description, RiskLevel risk, string packageName, AppxManager appx, bool systemCritical = false)
        : this(id, displayName, description, risk, new[] { packageName }, appx, systemCritical)
    {
    }

    public AppxRemoveAction(
        string id,
        string displayName,
        string description,
        RiskLevel risk,
        IReadOnlyList<string> packageNames,
        AppxManager appx,
        bool systemCritical = false)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Risk = risk;
        Category = "Bloatware";
        _packageNames = packageNames;
        _appx = appx;
        CanRevert = false;
        SystemCritical = systemCritical;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert { get; }
    public bool SystemCritical { get; }
    public IProgress<string>? Progress { get; set; }

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("installed?");

    public ActionPreview BuildPreview()
    {
        var packages = string.Join(", ", _packageNames);
        return ActionPreviewDefaults.FromAction(
            this,
            "installed",
            "removed",
            PreviewDiffKind.Irreversible,
            [
                new PreviewDiffRow
                {
                    Target = $"appx:{packages}",
                    Current = "installed",
                    Proposed = "removed",
                    Kind = PreviewDiffKind.Irreversible,
                    Unchanged = false
                }
            ]);
    }

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.RemovePackageAsync(_packageNames, cancellationToken, Progress).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorDetail);
        return ChangeResult.Ok("installed", "removed", "Package removed");
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail("AppX removal cannot be automatically reverted. Reinstall from Microsoft Store."));
}

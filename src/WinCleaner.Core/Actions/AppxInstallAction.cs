using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

/// <summary>Reinstalls an AppX / Store package via winget using a Store or winget ID.</summary>
public sealed class AppxInstallAction : IChangeAction
{
    private readonly WingetInstallerService _winget;
    private readonly string _installId;

    public AppxInstallAction(
        string id,
        string displayName,
        string description,
        RiskLevel risk,
        string installId,
        WingetInstallerService winget)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Risk = risk;
        Category = "Bloatware";
        _installId = installId;
        _winget = winget;
        CanRevert = false;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public RiskLevel Risk { get; }
    public string Category { get; }
    public bool CanRevert { get; }
    public bool IsOneShot => true;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("not-installed?");

    public ActionPreview BuildPreview() =>
        ActionPreviewDefaults.FromAction(
            this,
            "missing",
            "installed",
            PreviewDiffKind.Irreversible,
            [
                new PreviewDiffRow
                {
                    Target = $"winget:{_installId}",
                    Current = "missing",
                    Proposed = "installed",
                    Kind = PreviewDiffKind.Irreversible,
                    Unchanged = false
                }
            ]);

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _winget.InstallAsync(_installId, progress: null, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorMessage ?? result.Output);
        var msg = result.Status == WingetInstallStatus.AlreadyInstalled
            ? "Already installed"
            : "Package installed";
        return ChangeResult.Ok("missing", "installed", msg);
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail("AppX install cannot be auto-reverted; remove the package instead."));
}

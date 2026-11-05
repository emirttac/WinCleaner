using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public sealed class OneDriveUninstallAction : IChangeAction
{
    private readonly AppxManager _appx;
    private readonly Func<string, string> _localize;

    public OneDriveUninstallAction(AppxManager appx, Func<string, string>? localize = null)
    {
        _appx = appx;
        _localize = localize ?? (k => k);
    }

    public string Id => "onedrive-uninstall";
    public string DisplayName => _localize("blo.onedrive");
    public string Description => _localize("blo.onedriveConfirm");
    public RiskLevel Risk => RiskLevel.Dangerous;
    public string Category => "Bloatware";
    public bool CanRevert => false;

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>("installed?");

    public ActionPreview BuildPreview() =>
        ActionPreviewDefaults.FromAction(
            this,
            "installed",
            "uninstalled",
            PreviewDiffKind.Irreversible,
            [
                new PreviewDiffRow
                {
                    Target = "onedrive",
                    Current = "installed",
                    Proposed = "uninstalled",
                    Kind = PreviewDiffKind.Irreversible,
                    Unchanged = false
                }
            ]);

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.UninstallOneDriveAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorDetail);
        return ChangeResult.Ok("installed", "uninstalled", _localize("blo.onedriveOk"));
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail("OneDrive uninstall cannot be auto-reverted."));
}

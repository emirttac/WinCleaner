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
    public bool WritesMachine => true;
    public bool IsApplied() => !_appx.IsOneDriveInstalled();

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_appx.IsOneDriveInstalled() ? "installed" : "uninstalled");

    public ActionPreview BuildPreview()
    {
        var current = _appx.IsOneDriveInstalled() ? "installed" : "uninstalled";
        var unchanged = current == "uninstalled";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            "uninstalled",
            PreviewDiffKind.Irreversible,
            [
                new PreviewDiffRow
                {
                    Target = "onedrive",
                    Current = current,
                    Proposed = "uninstalled",
                    Kind = PreviewDiffKind.Irreversible,
                    Unchanged = unchanged
                }
            ]);
    }

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.UninstallOneDriveAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return ChangeResult.Fail(result.ErrorDetail);
        return ChangeResult.Ok("installed", "uninstalled", _localize("blo.onedriveOk"));
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail(_localize("blo.onedriveNoRevert")));
}

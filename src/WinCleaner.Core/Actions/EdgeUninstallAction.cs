using WinCleaner.Core.Models;
using WinCleaner.Core.Services;

namespace WinCleaner.Core.Actions;

public sealed class EdgeUninstallAction : IChangeAction
{
    private readonly AppxManager _appx;
    private readonly Func<string, string> _localize;

    public EdgeUninstallAction(AppxManager appx, Func<string, string>? localize = null)
    {
        _appx = appx;
        _localize = localize ?? (k => k);
    }

    public string Id => "edge-uninstall";
    public string DisplayName => _localize("blo.edge");
    public string Description => _localize("blo.edgeConfirm");
    public RiskLevel Risk => RiskLevel.Dangerous;
    public string Category => "Bloatware";
    public bool CanRevert => false;
    public bool WritesMachine => true;
    public bool IsApplied() => !_appx.IsEdgeBrowserInstalled();

    public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(_appx.IsEdgeBrowserInstalled() ? "installed" : "uninstalled");

    public ActionPreview BuildPreview()
    {
        var current = _appx.IsEdgeBrowserInstalled() ? "installed" : "uninstalled";
        var unchanged = current == "uninstalled";
        return ActionPreviewDefaults.FromAction(
            this,
            current,
            "uninstalled",
            PreviewDiffKind.Irreversible,
            [
                new PreviewDiffRow
                {
                    Target = "edge",
                    Current = current,
                    Proposed = "uninstalled",
                    Kind = PreviewDiffKind.Irreversible,
                    Unchanged = unchanged
                }
            ]);
    }

    public async Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        var result = await _appx.UninstallEdgeAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            var detail = $"{result.StdOut}\n{result.StdErr}";
            if (detail.Contains("EDGE_STILL_INSTALLED", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("still installed after uninstall", StringComparison.OrdinalIgnoreCase))
                return ChangeResult.Fail(_localize("blo.edgeStillInstalled"));
            return ChangeResult.Fail(string.IsNullOrWhiteSpace(result.ErrorDetail)
                ? _localize("blo.edgeFailed")
                : result.ErrorDetail);
        }
        return ChangeResult.Ok("installed", "uninstalled", _localize("blo.edgeOk"));
    }

    public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ChangeResult.Fail(_localize("blo.edgeNoRevert")));
}

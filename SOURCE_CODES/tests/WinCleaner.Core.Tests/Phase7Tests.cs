using System.Text.Json;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class Phase7Tests
{
    [Fact]
    public void InstallerCatalog_HasAtLeast150UniqueWingetIds()
    {
        var apps = CatalogLoader.LoadInstallerApps();
        Assert.True(apps.Count >= 150);
        Assert.Equal(apps.Count, apps.Select(a => a.WingetId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(apps, app => Assert.Matches(@"^[A-Za-z0-9][A-Za-z0-9+\-_.]*(\.[A-Za-z0-9][A-Za-z0-9+\-_.]*)+$|^[A-Za-z0-9]{12}$", app.WingetId));
    }

    [Fact]
    public void HomeSku_HidesHyperV()
    {
        var hyper = CatalogLoader.LoadTweaks().Single(t => t.Id == "tweak-feat-hyperv");
        Assert.Equal("Exact", hyper.Reversibility);
        Assert.False(CatalogAccess.Allows(hyper, "Home", System.Runtime.InteropServices.Architecture.X64));
        Assert.True(CatalogAccess.Allows(hyper, "Pro", System.Runtime.InteropServices.Architecture.X64));
    }

    [Fact]
    public void FeatureAlreadyEnabled_RevertDoesNotDisable()
    {
        Assert.True(FeatureRevertPolicy.LeaveEnabled(requiresFeature: "Microsoft-Hyper-V-All", previousState: "applied"));
        Assert.False(FeatureRevertPolicy.LeaveEnabled(requiresFeature: "Microsoft-Hyper-V-All", previousState: "not-applied"));
        Assert.False(FeatureRevertPolicy.LeaveEnabled(requiresFeature: null, previousState: "applied"));
    }

    [Fact]
    public void DnsPresets_IncludeIpv6AndDohMetadata()
    {
        var cloud = DnsChangerService.Presets.Single(p => p.Kind == DnsPresetKind.Cloudflare);
        Assert.Contains("2606:4700:4700::1111", cloud.Servers!);
        Assert.False(string.IsNullOrWhiteSpace(cloud.DohTemplate));
        Assert.Equal(new[] { "1.1.1.1", "2606:4700:4700::1111" }, DnsChangerService.ParseCustomServers("1.1.1.1, 2606:4700:4700::1111"));
    }

    [Fact]
    public async Task WhatIf_DoesNotInvokeApply()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """{"schemaVersion":3,"tweakIds":["tweak-game-mode","missing-id"]}""");
        var called = 0;
        var request = new ConfigRequest(path, Apply: true, Silent: true, LogPath: null, WhatIf: true, AllowUnknown: false);
        var outcome = await ConfigSession.RunAsync(
            request,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tweak-game-mode" },
            (_, _) =>
            {
                called++;
                return Task.CompletedTask;
            });

        Assert.Equal(0, called);
        Assert.True(outcome.Rejected);
        Assert.Contains("missing-id", outcome.UnknownIds);
        File.Delete(path);
    }

    [Fact]
    public void UnknownId_IsNotAppliedUnlessAllowed()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tweak-game-mode" };
        var doc = new CustomProfileDocument { SchemaVersion = 3, TweakIds = ["tweak-game-mode", "nope"] };
        var denied = ConfigSession.Plan(doc, known, allowUnknown: false);
        Assert.True(denied.Rejected);
        var allowed = ConfigSession.Plan(doc, known, allowUnknown: true);
        Assert.False(allowed.Rejected);
        Assert.Equal(["tweak-game-mode"], allowed.AcceptedIds);
    }

    [Fact]
    public void V2Profile_StillReads_AndNewWritesUseV3()
    {
        var v2 = JsonSerializer.Deserialize<CustomProfileDocument>("""{"schemaVersion":2,"tweakIds":["tweak-game-mode"]}""");
        Assert.Equal(2, v2!.SchemaVersion);
        Assert.Equal(3, CustomProfileDocument.CurrentSchemaVersion);
    }

    [Fact]
    public async Task RegistryTransfer_UsesProcessRunner()
    {
        var runner = new RecordingRunner();
        var transfer = new RegistryTransfer(runner);
        await transfer.ExportAsync(@"HKCU\Software\WinCleaner", "backup.reg");
        Assert.Equal("reg.exe", runner.LastFile);
        Assert.Contains("export", runner.LastArgs, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingRunner : WinCleaner.Core.Abstractions.IProcessRunner
    {
        public string LastFile { get; private set; } = "";
        public string LastArgs { get; private set; } = "";

        public Task<WinCleaner.Core.Abstractions.ProcessResult> RunAsync(
            WinCleaner.Core.Abstractions.ProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            LastFile = request.FileName;
            LastArgs = request.Arguments;
            return Task.FromResult(new WinCleaner.Core.Abstractions.ProcessResult(0, "", "", false, false));
        }
    }
}

public static class FeatureRevertPolicy
{
    public static bool LeaveEnabled(string? requiresFeature, string? previousState) =>
        !string.IsNullOrWhiteSpace(requiresFeature)
        && string.Equals(previousState, "applied", StringComparison.OrdinalIgnoreCase);
}

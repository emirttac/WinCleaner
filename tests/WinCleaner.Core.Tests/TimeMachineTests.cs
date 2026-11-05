using Microsoft.Win32;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class TimeMachineTests
{
    [Fact]
    public void RegistryPreview_DoesNotWrite_AndShowsCurrentToProposed()
    {
        const string hive = "HKCU";
        var path = @"Software\WinCleanerTests\TimeMachinePreview";
        const string name = "Sample";

        using (var key = Registry.CurrentUser.CreateSubKey(path))
            key!.SetValue(name, 5, RegistryValueKind.DWord);

        try
        {
            var registry = new RegistryManager();
            var entry = new TweakCatalogEntry
            {
                Id = "tweak-test-preview",
                DisplayNameKey = "x",
                DescriptionKey = "y",
                Category = "Test",
                Risk = "Safe",
                Type = "registry",
                Registry = new RegistryTweakSpec
                {
                    Hive = hive,
                    Path = path,
                    Name = name,
                    ValueKind = "DWord",
                    EnabledValue = "1",
                    DisabledValue = "0"
                }
            };

            var action = new RegistryChangeAction(entry, "Test preview", "Desc", registry);
            var preview = action.BuildPreview();

            Assert.Equal("1", preview.Diffs[0].Proposed);
            Assert.Equal("5", preview.Diffs[0].Current);
            Assert.False(preview.Diffs[0].Unchanged);
            Assert.False(preview.AlreadyApplied);
            Assert.Equal(PreviewDiffKind.Set, preview.Diffs[0].Kind);
            Assert.Equal("5", registry.GetValueAsString(hive, path, name));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void MultiRegistryPreview_ListsEachValue_WithoutWriting()
    {
        var path = @"Software\WinCleanerTests\TimeMachineMulti";
        using (var key = Registry.CurrentUser.CreateSubKey(path))
        {
            key!.SetValue("A", 0, RegistryValueKind.DWord);
            key.SetValue("B", 1, RegistryValueKind.DWord);
        }

        try
        {
            var registry = new RegistryManager();
            var entry = new TweakCatalogEntry
            {
                Id = "tweak-test-multi",
                DisplayNameKey = "x",
                DescriptionKey = "y",
                Category = "Test",
                Risk = "Safe",
                Type = "registry",
                Registries =
                [
                    new RegistryTweakSpec
                    {
                        Hive = "HKCU", Path = path, Name = "A",
                        ValueKind = "DWord", EnabledValue = "1", DisabledValue = "0"
                    },
                    new RegistryTweakSpec
                    {
                        Hive = "HKCU", Path = path, Name = "B",
                        ValueKind = "DWord", EnabledValue = "0", DisabledValue = "1",
                        DeleteValueOnEnable = true
                    }
                ]
            };

            var action = new MultiRegistryChangeAction(entry, "Multi", "Desc", registry);
            var preview = action.BuildPreview();
            Assert.Equal(2, preview.Diffs.Count);
            Assert.Equal("1", preview.Diffs[0].Proposed);
            Assert.Equal(ActionPreviewBuilder.AbsentMarker, preview.Diffs[1].Proposed);
            Assert.Equal(PreviewDiffKind.Delete, preview.Diffs[1].Kind);
            Assert.Equal("0", registry.GetValueAsString("HKCU", path, "A"));
            Assert.Equal("1", registry.GetValueAsString("HKCU", path, "B"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void ServicePreview_ReadsStartMode_WithoutChanging()
    {
        var mgr = new ServiceManager();
        var info = mgr.GetService("DiagTrack");
        if (info is null)
            return;

        var before = info.StartType;
        var action = new ServiceChangeAction(
            "svc-diagtrack",
            "DiagTrack",
            "desc",
            "Telemetry",
            RiskLevel.Safe,
            "DiagTrack",
            ServiceStartModeTarget.Disabled,
            mgr);

        var preview = action.BuildPreview();
        Assert.Contains("service:DiagTrack", preview.Diffs[0].Target, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Disabled", preview.Diffs[0].Proposed);
        Assert.Equal(before, mgr.GetService("DiagTrack")!.StartType);
    }

    [Fact]
    public void AppxPreview_IsIrreversible()
    {
        var action = new AppxRemoveAction(
            "app-test",
            "Test App",
            "desc",
            RiskLevel.Safe,
            "Microsoft.DoesNotExist.WinCleaner",
            new AppxManager());

        var preview = action.BuildPreview();
        Assert.False(preview.CanRevert);
        Assert.Equal(PreviewDiffKind.Irreversible, preview.Diffs[0].Kind);
        Assert.Equal("removed", preview.Diffs[0].Proposed);
    }

    [Fact]
    public void BatchPreview_RollsUpRiskAndSkipAlreadyApplied()
    {
        var applied = new FakeAction("a1", RiskLevel.Safe, applied: true, canRevert: true);
        var caution = new FakeAction("a2", RiskLevel.Caution, applied: false, canRevert: true, reboot: true);
        var danger = new FakeAction("a3", RiskLevel.Dangerous, applied: false, canRevert: false, antiCheat: true);

        var batch = ActionPreviewBuilder.Build([applied, caution, danger]);
        Assert.Equal(1, batch.AlreadyAppliedCount);
        Assert.Equal(2, batch.ChangeCount);
        Assert.Equal(1, batch.CautionCount);
        Assert.Equal(1, batch.DangerousCount);
        Assert.Equal(1, batch.IrreversibleCount);
        Assert.Equal(1, batch.RebootCount);
        Assert.Equal(1, batch.AntiCheatCount);
        Assert.Equal(2, batch.ToApply.Count);
    }

    [Fact]
    public void SnapshotStore_RoundTripsCapturedOldValue()
    {
        var file = Path.Combine(Path.GetTempPath(), "wc-snap-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SnapshotStore(file);
            var snap = store.Append("Gamer",
            [
                new SnapshotItem
                {
                    ActionId = "tweak-test",
                    DisplayName = "Test",
                    OldValue = "5",
                    NewValue = "1",
                    CanRevert = true,
                    Risk = "Safe"
                }
            ], restorePointCreated: true);

            var reloaded = new SnapshotStore(file);
            var latest = reloaded.GetLatest();
            Assert.NotNull(latest);
            Assert.Equal(snap.Id, latest!.Id);
            Assert.Equal("Gamer", latest.Name);
            Assert.True(latest.RestorePointCreated);
            Assert.Equal("5", latest.Items[0].OldValue);
            Assert.Equal("1", latest.Items[0].NewValue);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task RegistryRevert_UsesCapturedOldValue_NotCatalogDisabled()
    {
        const string hive = "HKCU";
        var path = @"Software\WinCleanerTests\TimeMachineRevert";
        const string name = "Sample";

        using (var key = Registry.CurrentUser.CreateSubKey(path))
            key!.SetValue(name, 7, RegistryValueKind.DWord);

        try
        {
            var registry = new RegistryManager();
            var entry = new TweakCatalogEntry
            {
                Id = "tweak-test-revert",
                DisplayNameKey = "x",
                DescriptionKey = "y",
                Category = "Test",
                Risk = "Safe",
                Type = "registry",
                Registry = new RegistryTweakSpec
                {
                    Hive = hive,
                    Path = path,
                    Name = name,
                    ValueKind = "DWord",
                    EnabledValue = "1",
                    DisabledValue = "0"
                }
            };

            var apply = new RegistryChangeAction(entry, "Test", "Desc", registry);
            var applied = await apply.ApplyAsync();
            Assert.True(applied.Success);
            Assert.Equal("7", applied.OldValue);
            Assert.Equal("1", registry.GetValueAsString(hive, path, name));

            var revert = new RegistryChangeAction(entry, "Test", "Desc", registry);
            revert.SeedPreviousState(applied.OldValue);
            var reverted = await revert.RevertAsync();
            Assert.True(reverted.Success);
            Assert.Equal("7", registry.GetValueAsString(hive, path, name));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void DnsPresetAction_PreviewMatchesDetect()
    {
        var dns = new DnsChangerService();
        var current = dns.DetectCurrentPreset();
        var target = current == DnsPresetKind.Cloudflare ? DnsPresetKind.Quad9 : DnsPresetKind.Cloudflare;
        var action = new DnsPresetChangeAction(dns, target, k => k);
        var preview = action.BuildPreview();
        Assert.Equal(current.ToString(), preview.Diffs[0].Current);
        Assert.Equal(target.ToString(), preview.Diffs[0].Proposed);
        Assert.True(action.CanRevert);
    }

    private sealed class FakeAction : IChangeAction
    {
        private readonly bool _applied;

        public FakeAction(
            string id,
            RiskLevel risk,
            bool applied,
            bool canRevert,
            bool reboot = false,
            bool antiCheat = false)
        {
            Id = id;
            DisplayName = id;
            Description = id;
            Risk = risk;
            Category = "Test";
            CanRevert = canRevert;
            RequiresReboot = reboot;
            RequiresAntiCheatConfirm = antiCheat;
            _applied = applied;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public RiskLevel Risk { get; }
        public string Category { get; }
        public bool CanRevert { get; }
        public bool RequiresReboot { get; }
        public bool RequiresAntiCheatConfirm { get; }

        public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ChangeResult.Ok());

        public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ChangeResult.Ok());

        public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_applied ? "applied" : "not-applied");

        public bool IsApplied() => _applied;
    }
}

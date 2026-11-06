using Microsoft.Win32;
using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class ProcessAndGateTests
{
    [Fact]
    public async Task ProcessRunner_Echo_ReturnsExitCodeOnlyAfterExit()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(new ProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/c echo hello-runner",
            TimeSpan.FromSeconds(10)));

        Assert.False(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello-runner", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRunner_Timeout_DoesNotReportExitCode()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(new ProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/c ping 127.0.0.1 -n 30",
            TimeSpan.FromMilliseconds(400)));

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task BlockedAction_IsRejected_AndDoesNotApply()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: false);
        var action = new FakeAction { Risk = RiskLevel.Blocked };

        var result = await harness.Executor.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("blocked", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, action.Applies);
        Assert.Equal(0, action.Reverts);
    }

    [Fact]
    public async Task DangerousApply_IsRejected_WhenSettingIsOff()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: false);
        var action = new FakeAction { Risk = RiskLevel.Dangerous };

        var result = await harness.Executor.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Equal(GateCodes.Dangerous, result.ErrorCode);
        Assert.Equal("danger.needSettings", result.Message);
        Assert.Equal(0, action.Applies);
    }

    [Fact]
    public async Task DangerousRevert_IsAllowed_WhenSettingIsOff()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: false);
        var action = new FakeAction { Risk = RiskLevel.Dangerous, CanRevert = true };

        var result = await harness.Executor.ExecuteAsync(action, isRevert: true);

        Assert.True(result.Success);
        Assert.Equal(0, action.Applies);
        Assert.Equal(1, action.Reverts);
    }

    [Fact]
    public async Task ExecuteMany_AppliesSafeAction_AndRecordsSnapshot()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: false);
        var action = new FakeAction { Risk = RiskLevel.Safe, CanRevert = true };

        var results = await harness.Executor.ExecuteManyAsync([action], snapshotName: "batch");

        var result = Assert.Single(results);
        Assert.True(result.Success);
        Assert.Equal(1, action.Applies);
    }

    [Fact]
    public void ServiceStartType_SendsScConfigArgument_WithoutStartingSc()
    {
        var fake = new FakeProcessRunner { Result = new ProcessResult(0, "", "", false, false) };
        var manager = new ServiceManager(fake);

        manager.SetStartType("WinDefend", ServiceStartModeTarget.Disabled);

        Assert.Contains(fake.Requests, request =>
            request.FileName == "sc.exe"
            && request.Arguments.Contains("config \"WinDefend\" start= disabled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DismRestoreHealth_SendsCommandText_WithoutStartingDism()
    {
        var fake = new FakeProcessRunner { Result = new ProcessResult(0, "", "", false, false) };
        var health = new SystemHealthService(fake);

        await health.RunDismRestoreHealthAsync(progress: null);

        var request = Assert.Single(fake.Requests);
        Assert.Contains("DISM.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/Online /Cleanup-Image /RestoreHealth", request.Arguments);
    }

    [Fact]
    public async Task AppxRemove_DoesNotConsultSystemCritical_AndStillInvokesRemoval()
    {
        var fake = new FakeProcessRunner { Result = new ProcessResult(0, "", "", false, false) };
        var appx = new AppxManager(fake);
        var action = new AppxRemoveAction("app-store", "Store", "critical", RiskLevel.Dangerous, "Microsoft.WindowsStore", appx);

        var result = await action.ApplyAsync();

        Assert.True(result.Success);
        var request = Assert.Single(fake.Requests);
        Assert.Contains("powershell.exe", request.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.WindowsStore", fake.Script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DnsRevert_ReappliesPreviousPresetEnum()
    {
        var dns = new FakeDns();
        var action = new DnsPresetChangeAction(dns, DnsPresetKind.Cloudflare, key => key);
        await action.ApplyAsync();
        action.SeedPreviousState(DnsPresetKind.Quad9.ToString());

        var result = await action.RevertAsync();

        Assert.True(result.Success);
        Assert.Equal(DnsPresetKind.Quad9, dns.Applied.Last());
    }

    private sealed class ExecutorHarness : IDisposable
    {
        public required ActionExecutor Executor { get; init; }
        public required string Root { get; init; }

        public static ExecutorHarness Create(
            bool allowDangerous,
            ISystemInfo? system = null,
            IConfirmationService? confirmation = null,
            Func<bool>? isAdministrator = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "wincleaner-tests", Guid.NewGuid().ToString("N"));
            var settings = new AppSettings
            {
                AllowDangerousActions = allowDangerous,
                AutoRestorePoint = false,
                LogFolder = Path.Combine(root, "logs")
            };
            var log = new ChangeLogService(root, settings.GetLogFolder());
            var snapshots = new SnapshotStore(Path.Combine(root, "snapshots.json"));
            var executor = new ActionExecutor(
                log,
                new RestorePointManager(),
                settings,
                snapshots,
                system,
                isAdministrator: isAdministrator)
            {
                Confirmation = confirmation
            };
            return new ExecutorHarness
            {
                Root = root,
                Executor = executor
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SystemCriticalAppx_IsRejectedByExecutor()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: true);
        var action = new AppxRemoveAction(
            "app-store", "Store", "critical", RiskLevel.Dangerous, "Microsoft.WindowsStore",
            new AppxManager(new FakeProcessRunner()), systemCritical: true);

        var result = await harness.Executor.ExecuteAsync(action);

        Assert.Equal(GateCodes.SystemCritical, result.ErrorCode);
    }

    [Fact]
    public async Task AntiCheat_Applies_WhenPreviewAlreadyConfirmed()
    {
        using var harness = ExecutorHarness.Create(allowDangerous: true);
        var action = new FakeAction { RequiresAntiCheatConfirm = true };

        var results = await harness.Executor.ExecuteManyAsync([action], antiCheatConfirmed: true);

        Assert.True(results[0].Success);
        Assert.Equal(1, action.Applies);
    }

    [Fact]
    public async Task AntiCheatBatch_AsksOnce_AndSkipsTheRest()
    {
        var confirm = new CountingConfirmation(code => code == GateCodes.AntiCheat);
        using var harness = ExecutorHarness.Create(allowDangerous: true, confirmation: confirm);
        var first = new FakeAction { RequiresAntiCheatConfirm = true };
        var second = new FakeAction { RequiresAntiCheatConfirm = true };

        var results = await harness.Executor.ExecuteManyAsync([first, second]);

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, first.Applies);
        Assert.Equal(1, second.Applies);
        Assert.Equal(1, confirm.Count);
    }

    [Fact]
    public async Task AntiCheat_IsRejected_WhenConfirmationReturnsFalse()
    {
        var confirm = new CountingConfirmation(_ => false);
        using var harness = ExecutorHarness.Create(allowDangerous: true, confirmation: confirm);
        var action = new FakeAction { RequiresAntiCheatConfirm = true };

        var result = await harness.Executor.ExecuteAsync(action);

        Assert.Equal(GateCodes.AntiCheat, result.ErrorCode);
        Assert.Equal(0, action.Applies);
        Assert.Equal(1, confirm.Count);
    }

    [Fact]
    public async Task Arm64Batch_AsksOnce_AndStopsWhenDeclined()
    {
        var confirm = new CountingConfirmation(_ => false);
        var system = new FakeSystem { OsArch = System.Runtime.InteropServices.Architecture.Arm64 };
        using var harness = ExecutorHarness.Create(allowDangerous: true, system: system, confirmation: confirm);
        var first = new FakeAction();
        var second = new FakeAction();

        var results = await harness.Executor.ExecuteManyAsync([first, second]);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(GateCodes.Arm64, result.ErrorCode));
        Assert.Equal(0, first.Applies);
        Assert.Equal(0, second.Applies);
        Assert.Equal(1, confirm.Count);
    }

    [Fact]
    public async Task Arm64Batch_AsksOnce_AndAppliesWhenConfirmed()
    {
        var confirm = new CountingConfirmation(_ => true);
        var system = new FakeSystem { OsArch = System.Runtime.InteropServices.Architecture.Arm64 };
        using var harness = ExecutorHarness.Create(allowDangerous: true, system: system, confirmation: confirm);
        var first = new FakeAction();
        var second = new FakeAction();

        var results = await harness.Executor.ExecuteManyAsync([first, second]);

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Equal(1, first.Applies);
        Assert.Equal(1, second.Applies);
        Assert.Equal(1, confirm.Count);
    }

    [Fact]
    public void HomeSku_DoesNotAllowHyperV_UnknownSkuStaysOpen_ServerHidesRecall()
    {
        var tweaks = CatalogLoader.LoadTweaks();
        var hyperv = tweaks.Single(t => t.Id == "tweak-feat-hyperv");
        var recall = tweaks.Single(t => t.Id == "tweak-disable-recall");
        var deferral = tweaks.Single(t => t.Id == "tweak-wu-recommended");
        var arch = System.Runtime.InteropServices.Architecture.X64;

        Assert.False(CatalogAccess.Allows(hyperv, "Home", arch));
        Assert.True(CatalogAccess.Allows(hyperv, "Pro", arch));
        Assert.True(CatalogAccess.Allows(hyperv, "Unknown", arch));
        Assert.False(CatalogAccess.Allows(recall, "Server", arch));
        Assert.True(CatalogAccess.Allows(recall, "Pro", arch));
        Assert.False(CatalogAccess.Allows(deferral, "Home", arch));
        Assert.True(CatalogAccess.Allows(deferral, "Pro", arch));
        Assert.True(CatalogAccess.Allows(deferral, "Unknown", arch));
    }

    [Fact]
    public void ApplyMeansDisable_UsesDisabledValueAsTheAppliedState()
    {
        var registry = new RegistryManager();
        var path = @"Software\WinCleanerTests\Gate\" + Guid.NewGuid().ToString("N");
        var entry = new TweakCatalogEntry
        {
            Id = "tweak-gate-disable",
            Risk = "Safe",
            Registry = new RegistryTweakSpec
            {
                Hive = "HKCU",
                Path = path,
                Name = "Flag",
                ValueKind = "DWord",
                EnabledValue = "1",
                DisabledValue = "0",
                ApplyMeansDisable = true
            }
        };
        var action = new RegistryChangeAction(entry, "t", "t", registry);
        try
        {
            registry.SetValue("HKCU", path, "Flag", 0, RegistryValueKind.DWord);
            Assert.True(action.IsApplied());
            registry.SetValue("HKCU", path, "Flag", 1, RegistryValueKind.DWord);
            Assert.False(action.IsApplied());
        }
        finally
        {
            registry.DeleteKeyTree("HKCU", path);
        }
    }

    [Theory]
    [InlineData(null, "Client", "Core", "Home")]
    [InlineData(null, "Client", "Professional", "Pro")]
    [InlineData(null, "Client", "EnterpriseS", "LTSC")]
    [InlineData(3, "Server", "ServerStandard", "Server")]
    [InlineData(null, "Client", "", "Unknown")]
    public void SkuMap_MapsEdition(int? productType, string installation, string edition, string expected)
    {
        Assert.Equal(expected, SkuMap.FromEdition(productType, installation, edition));
    }

    private sealed class FakeAction : IChangeAction
    {
        public string Id { get; init; } = "fake";
        public string DisplayName => "Fake";
        public string Description => "Fake";
        public RiskLevel Risk { get; init; }
        public string Category => "Test";
        public bool CanRevert { get; init; }
        public bool RequiresAntiCheatConfirm { get; init; }
        public int Applies { get; private set; }
        public int Reverts { get; private set; }

        public Task<ChangeResult> ApplyAsync(CancellationToken cancellationToken = default)
        {
            Applies++;
            return Task.FromResult(ChangeResult.Ok("old", "new"));
        }

        public Task<ChangeResult> RevertAsync(CancellationToken cancellationToken = default)
        {
            Reverts++;
            return Task.FromResult(ChangeResult.Ok("new", "old"));
        }

        public Task<string?> GetCurrentStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("old");
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public ProcessResult Result { get; init; } = new(0, "", "", false, false);
        public string Script { get; private set; } = "";

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            const string marker = "-File \"";
            var start = request.Arguments.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var path = request.Arguments[(start + marker.Length)..].TrimEnd('"');
                if (File.Exists(path))
                    Script = File.ReadAllText(path);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class FakeSystem : ISystemInfo
    {
        public int Build => 22631;
        public string Sku { get; init; } = "Pro";
        public System.Runtime.InteropServices.Architecture OsArch { get; init; }
    }

    private sealed class CountingConfirmation : IConfirmationService
    {
        private readonly Func<string, bool> _answer;
        public CountingConfirmation(Func<string, bool> answer) => _answer = answer;
        public int Count { get; private set; }

        public Task<bool> ConfirmAsync(string code, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(_answer(code));
        }
    }

    private sealed class FakeDns : IDnsPresetStore
    {
        public List<DnsPresetKind> Applied { get; } = [];
        public DnsPresetKind Current { get; set; } = DnsPresetKind.Dhcp;

        public DnsPresetKind DetectCurrentPreset() => Current;

        public DnsApplyResult ApplyPreset(DnsPresetKind kind)
        {
            Applied.Add(kind);
            Current = kind;
            return new DnsApplyResult { Success = true, Message = kind.ToString() };
        }
    }
}

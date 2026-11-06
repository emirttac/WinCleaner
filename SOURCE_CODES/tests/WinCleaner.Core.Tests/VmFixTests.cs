using System.Globalization;
using System.Text;
using Microsoft.Win32;
using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Operations;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class VmFixTests
{
    [Fact]
    public async Task AbsentRegistryValues_AreDeletedOnRevert_UnseededRevertFails()
    {
        var registry = new RegistryManager();
        var path = @"Software\WinCleanerTests\Absent\" + Guid.NewGuid().ToString("N");
        var entry = new TweakCatalogEntry
        {
            Id = "tweak-absent",
            Risk = "Safe",
            Registries =
            [
                Spec(path, "Alpha"),
                Spec(path, "Beta")
            ]
        };
        var action = new MultiRegistryChangeAction(entry, "t", "t", registry);
        try
        {
            registry.DeleteKeyTree("HKCU", path);
            var unseeded = await action.RevertAsync();
            Assert.False(unseeded.Success);
            Assert.Contains("No previous value", unseeded.Message);

            var applied = await action.ApplyAsync();
            Assert.True(applied.Success);
            Assert.Equal("1", registry.GetValueAsString("HKCU", path, "Alpha"));
            Assert.Equal("1", registry.GetValueAsString("HKCU", path, "Beta"));

            var reverted = await action.RevertAsync();
            Assert.True(reverted.Success);
            Assert.Null(registry.GetValueAsString("HKCU", path, "Alpha"));
            Assert.Null(registry.GetValueAsString("HKCU", path, "Beta"));
        }
        finally
        {
            registry.DeleteKeyTree("HKCU", path);
        }
    }

    [Fact]
    public async Task SingleRegistry_UnseededRevertFails_AbsentPriorDeletes()
    {
        var registry = new RegistryManager();
        var path = @"Software\WinCleanerTests\AbsentOne\" + Guid.NewGuid().ToString("N");
        var entry = new TweakCatalogEntry
        {
            Id = "tweak-absent-one",
            Risk = "Safe",
            Registry = Spec(path, "Flag")
        };
        var fresh = new RegistryChangeAction(entry, "t", "t", registry);
        var action = new RegistryChangeAction(entry, "t", "t", registry);
        try
        {
            var unseeded = await fresh.RevertAsync();
            Assert.False(unseeded.Success);

            Assert.True((await action.ApplyAsync()).Success);
            Assert.True((await action.RevertAsync()).Success);
            Assert.Null(registry.GetValueAsString("HKCU", path, "Flag"));
        }
        finally
        {
            registry.DeleteKeyTree("HKCU", path);
        }
    }

    [Fact]
    public async Task OptionalRegistryWrite_DoesNotRollBackTheRequiredValue()
    {
        var registry = new ScriptedRegistry();
        var entry = new TweakCatalogEntry
        {
            Id = "tweak-disable-widgets",
            Risk = "Safe",
            Registries =
            [
                Spec(@"Software\WinCleanerTests\Widgets", "TaskbarDa"),
                new RegistryTweakSpec
                {
                    Hive = "HKLM",
                    Path = @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds",
                    Name = "EnableFeeds",
                    ValueKind = "DWord",
                    EnabledValue = "0",
                    DisabledValue = "1",
                    OptionalForDetect = true
                }
            ]
        };
        var action = new MultiRegistryChangeAction(entry, "t", "t", registry);

        var result = await action.ApplyAsync();

        Assert.True(result.Success);
        Assert.Equal("1", registry.Values["TaskbarDa"]);
        Assert.False(registry.Values.ContainsKey("EnableFeeds"));
    }

    [Fact]
    public async Task CancelledPackageRemoval_IsNotReportedAsTimedOut()
    {
        var runner = new CapturingRunner(new ProcessResult(null, "", "", TimedOut: false, Cancelled: true));
        var appx = new AppxManager(runner);

        var result = await appx.RemovePackageAsync("Microsoft.GamingApp");

        Assert.False(result.Success);
        Assert.Equal("msg.processCancelled", result.StdErr);
        Assert.DoesNotContain("Timed out", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"WindowsPowerShell\v1.0\powershell.exe", runner.Request!.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-File", runner.Request.Arguments, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(3), runner.Request.Timeout);
        Assert.Contains("Get-Matching -AllUsers", runner.Script);
        Assert.DoesNotContain("Get-Matching -AllUsers", runner.Script.Replace("Get-Matching -AllUsers", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OneDriveScript_ProtectsTheSyncFolder_AndStartsFromAFile()
    {
        var runner = new CapturingRunner(new ProcessResult(0, "removed", "", false, false));
        var appx = new AppxManager(runner);

        var result = await appx.UninstallOneDriveAsync();

        Assert.True(result.Success);
        Assert.Contains("-File", runner.Request!.Arguments, StringComparison.Ordinal);
        Assert.Contains("icacls.exe", runner.Script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Set-Acl", runner.Script, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop-Process -Name explorer", runner.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taskkill", runner.Script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRunner_UsesSystem32AsTheWorkingDirectory()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(new ProcessRequest(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/c cd",
            TimeSpan.FromSeconds(15)));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("System32", result.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleOutput_PrefersValidUtf8_AndKeepsOemBytes()
    {
        Assert.Equal("ş", ConsoleOutput.Decode(new byte[] { 0xC5, 0x9F }));

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            Assert.Equal("ş", ConsoleOutput.Decode(oem.GetBytes("ş")));
            Assert.Equal("Erişim", ConsoleOutput.Decode(Encoding.UTF8.GetBytes("Erişim")));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void IpReset_AccessDeniedWithRebootHint_IsAWarning()
    {
        const string denied = """
            Resetting Interface, Failed.
            Access is denied.

            Restart the computer to complete this action.
            """;
        const string other = """
            Resetting Interface, Failed.
            The system cannot find the path specified.

            Restart the computer to complete this action.
            """;
        const string turkish = """
            Arabirim sıfırlanıyor, başarısız.
            Erişim engellendi.
            Değişikliklerin uygulanması için bilgisayarı yeniden başlatın.
            """;

        Assert.True(SystemHealthService.IsBenignIpResetFailure(denied));
        Assert.True(SystemHealthService.IsBenignIpResetFailure(turkish));
        Assert.False(SystemHealthService.IsBenignIpResetFailure(other));
    }

    [Fact]
    public void SafeDirSize_CountsReadableFiles_AndSkipsReparsePoints()
    {
        var root = Path.Combine(Path.GetTempPath(), "wc-size-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "wc-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[8]);
            File.WriteAllBytes(Path.Combine(outside, "c.bin"), new byte[1000]);
            var linked = false;
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);
                linked = true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            var size = SystemHealthService.SafeDirSize(root, CancellationToken.None);
            Assert.Equal(8, size);
            Assert.True(linked || size == 8);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
            try { Directory.Delete(outside, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task OperationLog_WritesProgressBeforeDone()
    {
        var log = new ListLog();
        var runner = new OperationRunner(log, new X64System(), () => null);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new PostingContext());
        try
        {
            var result = await runner.EnqueueAsync(new OperationRequest("disk", true, async ctx =>
            {
                ctx.Progress.Report(new OperationStep("users 12", null));
                await Task.Yield();
                ctx.Progress.Report(new OperationStep("windows 99", null));
            }));
            Assert.True(result.Success);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var progress = log.Lines.FindIndex(line => line.Contains("users 12", StringComparison.Ordinal));
        var later = log.Lines.FindIndex(line => line.Contains("windows 99", StringComparison.Ordinal));
        var done = log.Lines.FindIndex(line => line.StartsWith("schema=2 done", StringComparison.Ordinal));
        Assert.True(progress >= 0 && later > progress && done > later);
    }

    [Fact]
    public void CoreParking_CatalogDoesNotChainCommands_AndMissingSettingIsRecognized()
    {
        var tweak = CatalogLoader.LoadTweaks().Single(t => t.Id == "tweak-core-parking");
        Assert.Equal("powercfg", tweak.Command!.FileName);
        Assert.DoesNotContain("&", tweak.Command.Arguments);
        Assert.True(CoreParking.IsSettingMissing("The power setting does not exist."));
        Assert.False(CoreParking.IsSettingMissing("Power Scheme GUID: 381b4222"));
        Assert.Equal(100, CommandStateProbe.ParsePowerCfgAcSettingIndex(
            "Minimum: 0x0\nMaximum: 0x64\nIncrement: 0x1\nCurrent AC: 0x64\nCurrent DC: 0x64\n"));

        var widgets = CatalogLoader.LoadTweaks().Single(t => t.Id == "tweak-disable-widgets");
        Assert.False(widgets.Registries!.Single(r => r.Name == "TaskbarDa").OptionalForDetect);
        Assert.True(widgets.Registries!.Single(r => r.Name == "AllowNewsAndInterests").OptionalForDetect);
        Assert.True(widgets.Registries!.Single(r => r.Name == "EnableFeeds").OptionalForDetect);
    }

    private static RegistryTweakSpec Spec(string path, string name) => new()
    {
        Hive = "HKCU",
        Path = path,
        Name = name,
        ValueKind = "DWord",
        EnabledValue = "1",
        DisabledValue = "0"
    };

    private sealed class ScriptedRegistry : IRegistryAccess
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetValueAsString(string hive, string path, string name) =>
            Values.TryGetValue(name, out var value) ? value : null;

        public void SetValue(string hive, string path, string name, object value, RegistryValueKind kind)
        {
            if (name == "EnableFeeds")
                throw new UnauthorizedAccessException("Attempted to perform an unauthorized operation.");
            Values[name] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        public void DeleteValue(string hive, string path, string name) => Values.Remove(name);
    }

    private sealed class CapturingRunner : IProcessRunner
    {
        private readonly ProcessResult _result;
        public CapturingRunner(ProcessResult result) => _result = result;
        public ProcessRequest? Request { get; private set; }
        public string Script { get; private set; } = "";

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            var marker = "-File \"";
            var start = request.Arguments.IndexOf(marker, StringComparison.Ordinal);
            if (start >= 0)
            {
                var path = request.Arguments[(start + marker.Length)..].TrimEnd('"');
                if (File.Exists(path))
                    Script = File.ReadAllText(path);
            }

            return Task.FromResult(_result);
        }
    }

    private sealed class ListLog : ILogSink
    {
        public List<string> Lines { get; } = [];
        public void Write(string line) => Lines.Add(line);
    }

    private sealed class X64System : ISystemInfo
    {
        public int Build => 22631;
        public string Sku => "Pro";
        public System.Runtime.InteropServices.Architecture OsArch => System.Runtime.InteropServices.Architecture.X64;
    }

    private sealed class PostingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => ThreadPool.QueueUserWorkItem(_ => d(state));
    }
}

using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Operations;

namespace WinCleaner.Core.Tests;

public class OperationRunnerTests
{
    [Fact]
    public async Task Writes_RunOneAtATime_AndQueuedItemCanBeCancelled()
    {
        var runner = new OperationRunner(new MemoryLog(), new X64System(), () => null);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runner.EnqueueAsync(new OperationRequest("first", true, async ctx =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(ctx.Token);
        }));
        await started.Task;
        var second = runner.EnqueueAsync(new OperationRequest("second", true, _ => Task.CompletedTask));
        await Task.Delay(50);
        Assert.Single(runner.Queue);
        Assert.True(runner.CancelQueued(runner.Queue[0].Id));
        release.TrySetResult();
        var firstResult = await first;
        var secondResult = await second;
        Assert.True(firstResult.Success);
        Assert.True(secondResult.Cancelled);
    }

    private sealed class MemoryLog : ILogSink
    {
        public void Write(string line) { }
    }

    private sealed class X64System : ISystemInfo
    {
        public int Build => 22631;
        public string Sku => "Pro";
        public System.Runtime.InteropServices.Architecture OsArch => System.Runtime.InteropServices.Architecture.X64;
    }
}

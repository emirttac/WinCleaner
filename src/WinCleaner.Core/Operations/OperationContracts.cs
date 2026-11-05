namespace WinCleaner.Core.Operations;

public interface IOperationRunner
{
    bool IsBusy { get; }
    IReadOnlyList<QueuedOperation> Queue { get; }
    event EventHandler<OperationStep>? StepReported;
    Task<OperationResult> EnqueueAsync(OperationRequest request, CancellationToken cancellationToken = default);
    bool CancelQueued(Guid id);
    bool MoveQueued(Guid id, int delta);
}

public sealed record OperationRequest(string Name, bool WritesSystem, Func<OperationContext, Task> Body);

public sealed record OperationContext(IProgress<OperationStep> Progress, ILogSink Log, CancellationToken Token);

public sealed record OperationStep(string Message, double? Percent);

public sealed record OperationResult(bool Success, bool Cancelled, string? Message)
{
    public static OperationResult Ok() => new(true, false, null);
    public static OperationResult Fail(string message) => new(false, false, message);
    public static OperationResult Canceled() => new(false, true, null);
}

public interface ILogSink
{
    void Write(string line);
}

public sealed class QueuedOperation
{
    public QueuedOperation(OperationRequest request)
    {
        Id = Guid.NewGuid();
        Request = request;
    }

    public Guid Id { get; }
    public OperationRequest Request { get; }
    public bool Cancelled { get; set; }
    internal TaskCompletionSource<bool> Turn { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public static class Arm64Lease
{
    private static readonly AsyncLocal<bool> ConfirmedFlag = new();
    public static bool IsConfirmed => ConfirmedFlag.Value;
    public static void Set(bool confirmed) => ConfirmedFlag.Value = confirmed;
}

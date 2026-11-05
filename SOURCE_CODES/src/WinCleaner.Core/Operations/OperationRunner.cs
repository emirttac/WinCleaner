using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Actions;

namespace WinCleaner.Core.Operations;

public sealed class OperationRunner : IOperationRunner
{
    private readonly object _gate = new();
    private readonly List<QueuedOperation> _queue = [];
    private int _activeWriters;
    private readonly ILogSink _log;
    private readonly ISystemInfo _system;
    private readonly Func<IConfirmationService?> _confirmation;

    public OperationRunner(ILogSink log, ISystemInfo system, Func<IConfirmationService?> confirmation)
    {
        _log = log;
        _system = system;
        _confirmation = confirmation;
    }

    public bool IsBusy { get; private set; }
    public event EventHandler<OperationStep>? StepReported;

    public IReadOnlyList<QueuedOperation> Queue
    {
        get
        {
            lock (_gate)
                return _queue.ToList();
        }
    }

    public async Task<OperationResult> EnqueueAsync(OperationRequest request, CancellationToken cancellationToken = default)
    {
        var item = new QueuedOperation(request);
        bool startNow;
        lock (_gate)
        {
            if (request.WritesSystem && _activeWriters > 0)
            {
                _queue.Add(item);
                startNow = false;
            }
            else
            {
                if (request.WritesSystem)
                    _activeWriters++;
                startNow = true;
            }
        }

        if (!startNow)
        {
            using var reg = cancellationToken.Register(() => CancelQueued(item.Id));
            var turn = await item.Turn.Task.ConfigureAwait(false);
            if (!turn || item.Cancelled)
                return OperationResult.Canceled();
        }

        try
        {
            return await RunAsync(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(request.WritesSystem);
        }
    }

    public bool CancelQueued(Guid id)
    {
        QueuedOperation? item = null;
        lock (_gate)
        {
            item = _queue.FirstOrDefault(q => q.Id == id);
            if (item is null)
                return false;
            _queue.Remove(item);
        }

        item.Cancelled = true;
        item.Turn.TrySetResult(false);
        return true;
    }

    public bool MoveQueued(Guid id, int delta)
    {
        if (delta == 0)
            return false;
        lock (_gate)
        {
            var index = _queue.FindIndex(q => q.Id == id);
            if (index < 0)
                return false;
            var target = index + delta;
            if (target < 0 || target >= _queue.Count)
                return false;
            (_queue[index], _queue[target]) = (_queue[target], _queue[index]);
            return true;
        }
    }

    private async Task<OperationResult> RunAsync(QueuedOperation item, CancellationToken cancellationToken)
    {
        IsBusy = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progress = new Progress<OperationStep>(step =>
        {
            _log.Write(item.Request.Name + ": " + step.Message);
            StepReported?.Invoke(this, step);
        });
        var context = new OperationContext(progress, _log, linked.Token);
        var leased = false;
        try
        {
            if (item.Request.WritesSystem && _system.OsArch == System.Runtime.InteropServices.Architecture.Arm64 && !Arm64Lease.IsConfirmed)
            {
                var confirm = _confirmation();
                var accepted = confirm is not null &&
                               await confirm.ConfirmAsync(GateCodes.Arm64, linked.Token).ConfigureAwait(false);
                if (!accepted)
                    return OperationResult.Fail(GateCodes.Arm64);
                Arm64Lease.Set(true);
                leased = true;
            }

            _log.Write("schema=2 start " + item.Request.Name);
            await item.Request.Body(context).ConfigureAwait(false);
            _log.Write("schema=2 done " + item.Request.Name);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            _log.Write("schema=2 cancel " + item.Request.Name);
            return OperationResult.Canceled();
        }
        catch (Exception ex)
        {
            _log.Write("schema=2 fail " + item.Request.Name + " " + ex.Message);
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            if (leased)
                Arm64Lease.Set(false);
            IsBusy = Queue.Count > 0 || _activeWriters > 0;
        }
    }

    private void Release(bool write)
    {
        QueuedOperation? next = null;
        lock (_gate)
        {
            if (write && _activeWriters > 0)
                _activeWriters--;
            if (_activeWriters == 0 && _queue.Count > 0)
            {
                next = _queue[0];
                _queue.RemoveAt(0);
                _activeWriters++;
            }
        }

        next?.Turn.TrySetResult(true);
    }
}

public sealed class SessionLineLog : ILogSink
{
    private readonly string _path;
    private readonly object _gate = new();

    public SessionLineLog(string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        _path = Path.Combine(logFolder, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Write("schema=2");
    }

    public string PathToFile => _path;

    public void Write(string line)
    {
        lock (_gate)
            File.AppendAllText(_path, line + Environment.NewLine);
    }
}

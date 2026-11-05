namespace WinCleaner.Core.Services;

/// <summary>
/// Marshals a finished probe back to the UI. <see cref="Invoke"/> is the synchronous path and must not be used for startup probes.
/// </summary>
public interface IUiMarshal
{
    bool CheckAccess();
    void Invoke(Action action);
    void Post(Action action);
}

public sealed record AppliedProbe(string Id, Func<bool> Read);

/// <summary>
/// Bulk, cached <c>IsApplied</c> reads. Probes run on the thread pool. Results are published with <see cref="IUiMarshal.Post"/>, never <see cref="IUiMarshal.Invoke"/>.
/// </summary>
public sealed class AppliedStateCache
{
    private readonly Dictionary<string, bool> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool TryGet(string id, out bool applied)
    {
        lock (_gate)
            return _values.TryGetValue(id, out applied);
    }

    public void Set(string id, bool applied)
    {
        lock (_gate)
            _values[id] = applied;
    }

    public async Task<IReadOnlyDictionary<string, bool>> ProbeAsync(
        IReadOnlyList<AppliedProbe> probes,
        IUiMarshal marshal,
        Action<IReadOnlyDictionary<string, bool>> publish,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await Task.Run(() =>
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var probe in probes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool applied;
                try
                {
                    applied = probe.Read();
                }
                catch
                {
                    applied = false;
                }

                map[probe.Id] = applied;
            }

            lock (_gate)
            {
                foreach (var pair in map)
                    _values[pair.Key] = pair.Value;
            }

            return (IReadOnlyDictionary<string, bool>)map;
        }, cancellationToken).ConfigureAwait(false);

        if (marshal.CheckAccess())
            publish(snapshot);
        else
            marshal.Post(() => publish(snapshot));

        return snapshot;
    }
}

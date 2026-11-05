using System.Text.Json;
using WinCleaner.Core.Models;

namespace WinCleaner.Core.Services;

public sealed class SnapshotItem
{
    public string ActionId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool CanRevert { get; set; }
    public string Risk { get; set; } = nameof(RiskLevel.Safe);
}

public sealed class ChangeSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool RestorePointCreated { get; set; }
    public List<SnapshotItem> Items { get; set; } = [];

    public int RevertableCount => Items.Count(i => i.CanRevert);
}

public sealed class SnapshotStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private List<ChangeSnapshot> _snapshots = [];

    public SnapshotStore(AppSettings settings)
        : this(Path.Combine(settings.GetDataFolder(), "snapshots.json"))
    {
    }

    public SnapshotStore(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
        Load();
    }

    public event Action? Changed;

    public IReadOnlyList<ChangeSnapshot> GetAll()
    {
        lock (_lock)
            return _snapshots.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public ChangeSnapshot? GetLatest()
    {
        lock (_lock)
            return _snapshots.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
    }

    public ChangeSnapshot? GetById(string id)
    {
        lock (_lock)
            return _snapshots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ChangeSnapshot Append(string name, IReadOnlyList<SnapshotItem> items, bool restorePointCreated)
    {
        var snapshot = new ChangeSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(name) ? $"Manual · {DateTime.Now:HH:mm}" : name,
            CreatedAt = DateTimeOffset.Now,
            RestorePointCreated = restorePointCreated,
            Items = items.ToList()
        };

        lock (_lock)
        {
            _snapshots.Add(snapshot);
            Persist();
        }

        RaiseChanged();
        return snapshot;
    }

    public void Remove(string snapshotId)
    {
        lock (_lock)
        {
            var removed = _snapshots.RemoveAll(s =>
                string.Equals(s.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;
            Persist();
        }

        RaiseChanged();
    }

    public void RemoveItem(string snapshotId, string actionId)
    {
        lock (_lock)
        {
            var snap = _snapshots.FirstOrDefault(s =>
                string.Equals(s.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
            if (snap is null)
                return;

            snap.Items.RemoveAll(i =>
                string.Equals(i.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            if (snap.Items.Count == 0)
                _snapshots.Remove(snap);
            Persist();
        }

        RaiseChanged();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<ChangeSnapshot>>(json);
            if (loaded is not null)
                _snapshots = loaded;
        }
        catch
        {
            _snapshots = [];
        }
    }

    private void Persist()
    {
        var json = JsonSerializer.Serialize(_snapshots, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch { /* subscribers must not break apply */ }
    }
}

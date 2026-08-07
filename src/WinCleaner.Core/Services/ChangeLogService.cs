using System.Text.Json;
using WinCleaner.Core.Actions;

namespace WinCleaner.Core.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "tr";
    public bool AutoRestorePoint { get; set; } = true;
    public bool AllowDangerousActions { get; set; } = false;
    public string? LogFolder { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = false;

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCleaner", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public string GetLogFolder()
    {
        if (!string.IsNullOrWhiteSpace(LogFolder))
            return LogFolder;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCleaner", "logs");
    }

    public string GetDataFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCleaner");
}

public sealed class ChangeLogService
{
    private readonly List<ChangeLogEntry> _session = new();
    private readonly List<ChangeLogEntry> _journal = new();
    private readonly string _sessionFile;
    private readonly string _journalFile;
    private readonly object _lock = new();

    public ChangeLogService(AppSettings settings)
    {
        var folder = settings.GetLogFolder();
        Directory.CreateDirectory(folder);
        _sessionFile = Path.Combine(folder, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        var dataFolder = settings.GetDataFolder();
        Directory.CreateDirectory(dataFolder);
        _journalFile = Path.Combine(dataFolder, "undo_history.json");
        LoadJournal();
    }

    public event Action? Changed;

    /// <summary>
    /// Current-session audit trail plus persisted journal entries from prior runs
    /// (deduped by ActionId+Timestamp so in-session journal sync is not double-listed).
    /// </summary>
    public IReadOnlyList<ChangeLogEntry> SessionEntries
    {
        get
        {
            lock (_lock)
            {
                var sessionKeys = new HashSet<(string Id, DateTimeOffset Ts)>(
                    _session.Select(e => (e.ActionId, e.Timestamp)));
                var merged = new List<ChangeLogEntry>(_journal.Count + _session.Count);
                foreach (var j in _journal)
                {
                    if (!sessionKeys.Contains((j.ActionId, j.Timestamp)))
                        merged.Add(j);
                }
                merged.AddRange(_session);
                return merged;
            }
        }
    }

    /// <summary>Persisted revertable applies, newest first (one entry per ActionId).</summary>
    public IReadOnlyList<ChangeLogEntry> GetRevertableEntries()
    {
        lock (_lock)
        {
            return _journal
                .Where(e => e.CanRevert && e.Success)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }

    public void Append(ChangeLogEntry entry)
    {
        lock (_lock)
        {
            _session.Add(entry);
            PersistSession();

            if (entry.CanRevert && entry.Success)
            {
                // Keep latest revertable state per action id.
                _journal.RemoveAll(e => string.Equals(e.ActionId, entry.ActionId, StringComparison.OrdinalIgnoreCase));
                _journal.Add(entry);
                PersistJournal();
            }
        }

        try { Changed?.Invoke(); }
        catch { /* subscribers must not break apply */ }
    }

    public void RemoveFromJournal(string actionId)
    {
        lock (_lock)
        {
            var removed = _journal.RemoveAll(e =>
                string.Equals(e.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                PersistJournal();
        }

        try { Changed?.Invoke(); }
        catch { }
    }

    /// <summary>Latest journalled OldValue for a successful, revertable apply of this action.</summary>
    public ChangeLogEntry? GetLatestRevertable(string actionId)
    {
        lock (_lock)
        {
            for (var i = _journal.Count - 1; i >= 0; i--)
            {
                var e = _journal[i];
                if (e.CanRevert && e.Success &&
                    string.Equals(e.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            for (var i = _session.Count - 1; i >= 0; i--)
            {
                var e = _session[i];
                if (e.CanRevert && e.Success &&
                    string.Equals(e.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                    return e;
            }

            return null;
        }
    }

    public void ClearSessionMemory()
    {
        lock (_lock)
        {
            _session.Clear();
        }
    }

    public void ExportSessionReport(string? path = null, bool asText = false)
    {
        lock (_lock)
        {
            var entries = new List<ChangeLogEntry>(_journal.Count + _session.Count);
            entries.AddRange(_journal);
            entries.AddRange(_session);

            var folder = Path.GetDirectoryName(_sessionFile)
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCleaner", "logs");
            Directory.CreateDirectory(folder);

            path ??= Path.Combine(
                folder,
                asText
                    ? $"change-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
                    : $"change-report-{DateTime.Now:yyyyMMdd-HHmmss}.json");

            if (asText)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("WinCleaner change report");
                sb.AppendLine($"Generated: {DateTimeOffset.Now:O}");
                sb.AppendLine($"Entries: {entries.Count}");
                sb.AppendLine(new string('-', 60));
                foreach (var e in entries)
                {
                    sb.AppendLine($"[{e.Timestamp:yyyy-MM-dd HH:mm:ss}] {(e.Success ? "OK" : "FAIL")} {e.DisplayName} ({e.ActionId})");
                    sb.AppendLine($"  Category: {e.Category}");
                    sb.AppendLine($"  Old → New: {e.OldValue ?? "-"} → {e.NewValue ?? "-"}");
                    if (!string.IsNullOrWhiteSpace(e.Message))
                        sb.AppendLine($"  Message: {e.Message}");
                    sb.AppendLine($"  CanRevert: {e.CanRevert}");
                    sb.AppendLine();
                }
                File.WriteAllText(path, sb.ToString());
            }
            else
            {
                var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }

            LastExportPath = path;
        }
    }

    public string? LastExportPath { get; private set; }

    private void LoadJournal()
    {
        try
        {
            if (!File.Exists(_journalFile))
                return;

            var json = File.ReadAllText(_journalFile);
            var loaded = JsonSerializer.Deserialize<List<ChangeLogEntry>>(json);
            if (loaded is null)
                return;

            _journal.Clear();
            _journal.AddRange(loaded.Where(e => e.CanRevert && e.Success));
        }
        catch
        {
            // Corrupt journal must not block startup.
        }
    }

    private void PersistSession()
    {
        var json = JsonSerializer.Serialize(_session, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_sessionFile, json);
    }

    private void PersistJournal()
    {
        var json = JsonSerializer.Serialize(_journal, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_journalFile, json);
    }
}

public sealed class ActionExecutor
{
    private readonly ChangeLogService _log;
    private readonly RestorePointManager _restore;
    private readonly AppSettings _settings;

    public ActionExecutor(ChangeLogService log, RestorePointManager restore, AppSettings settings)
    {
        _log = log;
        _restore = restore;
        _settings = settings;
    }

    public async Task<ChangeResult> ExecuteAsync(IChangeAction action, bool isRevert = false, CancellationToken ct = default)
    {
        if (action.Risk == Models.RiskLevel.Blocked)
            return ChangeResult.Fail("This action is blocked because it can break the system.");

        if (action.Risk == Models.RiskLevel.Dangerous && !_settings.AllowDangerousActions && !isRevert)
            return ChangeResult.Fail("Dangerous actions are disabled in Settings.");

        // Never block the UI thread: restore + registry/service work run on the thread pool.
        if (!isRevert && _settings.AutoRestorePoint && _restore.ShouldCreateNow())
        {
            try
            {
                await _restore.CreateRestorePointAsync($"WinCleaner: {action.DisplayName}", ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Restore failure must not abort the tweak
            }
        }

        ChangeResult result;
        try
        {
            result = await Task.Run(async () =>
            {
                return isRevert
                    ? await action.RevertAsync(ct).ConfigureAwait(false)
                    : await action.ApplyAsync(ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = ChangeResult.Fail(ex.Message);
        }

        _log.Append(new ChangeLogEntry
        {
            ActionId = action.Id,
            DisplayName = action.DisplayName,
            Timestamp = DateTimeOffset.Now,
            OldValue = result.OldValue,
            NewValue = result.NewValue,
            CanRevert = action.CanRevert && result.Success && !isRevert,
            Success = result.Success,
            Message = result.Message,
            Category = action.Category
        });

        if (isRevert && result.Success)
            _log.RemoveFromJournal(action.Id);

        return result;
    }

    public async Task<IReadOnlyList<ChangeResult>> ExecuteManyAsync(
        IEnumerable<IChangeAction> actions,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<ChangeResult>();
        var list = actions.ToList();

        if (_settings.AutoRestorePoint && list.Count > 0 && _restore.ShouldCreateNow())
        {
            progress?.Report("Creating restore point...");
            try
            {
                await _restore.CreateRestorePointAsync($"WinCleaner batch ({list.Count} changes)", ct)
                    .ConfigureAwait(false);
            }
            catch { }
        }

        var prev = _settings.AutoRestorePoint;
        _settings.AutoRestorePoint = false; // avoid per-item restore during batch
        try
        {
            foreach (var action in list)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(action.DisplayName);
                results.Add(await ExecuteAsync(action, false, ct).ConfigureAwait(false));
            }
        }
        finally
        {
            _settings.AutoRestorePoint = prev;
        }

        return results;
    }
}

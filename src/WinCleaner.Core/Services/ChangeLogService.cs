using System.Text.Json;
using WinCleaner.Core.Abstractions;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Operations;

namespace WinCleaner.Core.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "tr";
    public bool AutoRestorePoint { get; set; } = true;
    public bool AllowDangerousActions { get; set; } = false;
    public string? LogFolder { get; set; }
    public bool CheckUpdatesOnStartup { get; set; } = false;
    /// <summary>Off, SessionOnce, BeforeIrreversible, or BeforeEachBatch. Missing JSON stays SessionOnce.</summary>
    public string RestorePointPolicy { get; set; } = "SessionOnce";
    /// <summary>Dark, Light, or System. Missing JSON stays Dark.</summary>
    public string Theme { get; set; } = "Dark";

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

public sealed class JournalDocument
{
    public int SchemaVersion { get; set; } = 2;
    public List<ChangeLogEntry> Entries { get; set; } = [];
}

public sealed class ChangeLogService
{
    private readonly List<ChangeLogEntry> _session = new();
    private readonly List<ChangeLogEntry> _journal = new();
    private readonly string _sessionFile;
    private readonly string _journalFile;
    private readonly object _lock = new();

    public ChangeLogService(AppSettings settings)
        : this(settings.GetDataFolder(), settings.GetLogFolder())
    {
    }

    internal ChangeLogService(string dataFolder, string logFolder)
    {
        Directory.CreateDirectory(logFolder);
        _sessionFile = Path.Combine(logFolder, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.json");
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
    public IReadOnlyList<ChangeLogEntry> GetPartialEntries()
    {
        lock (_lock)
        {
            return _journal
                .Where(e => e.ErrorCode == GateCodes.Partial)
                .ToList();
        }
    }

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

            if ((entry.CanRevert && entry.Success) || entry.ErrorCode == GateCodes.Partial)
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
            List<ChangeLogEntry>? loaded = null;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    loaded = JsonSerializer.Deserialize<List<ChangeLogEntry>>(json);
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    loaded = JsonSerializer.Deserialize<JournalDocument>(json)?.Entries;
            }

            if (loaded is null)
                return;

            _journal.Clear();
            _journal.AddRange(loaded.Where(e => e.CanRevert && e.Success));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Journal load failed: " + ex.Message);
            try
            {
                if (File.Exists(_journalFile))
                    File.Move(_journalFile, _journalFile + ".bad", overwrite: true);
            }
            catch (Exception moveEx)
            {
                System.Diagnostics.Trace.WriteLine("Journal backup failed: " + moveEx.Message);
            }
        }
    }

    private void PersistSession()
    {
        var json = JsonSerializer.Serialize(_session, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_sessionFile, json);
    }

    private void PersistJournal()
    {
        var json = JsonSerializer.Serialize(new JournalDocument { SchemaVersion = 2, Entries = _journal }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_journalFile, json);
    }
}

public sealed class ActionExecutor
{
    private readonly ChangeLogService _log;
    private readonly RestorePointManager _restore;
    private readonly AppSettings _settings;
    private readonly SnapshotStore _snapshots;
    private readonly IActionGate _gate;
    private readonly ISystemInfo _system;
    private readonly IFeatureProbe _features;
    private readonly Func<bool> _isAdmin;

    public ActionExecutor(
        ChangeLogService log,
        RestorePointManager restore,
        AppSettings settings,
        SnapshotStore snapshots,
        ISystemInfo? system = null,
        IActionGate? gate = null,
        IFeatureProbe? features = null,
        Func<bool>? isAdministrator = null)
    {
        _log = log;
        _restore = restore;
        _settings = settings;
        _snapshots = snapshots;
        _system = system ?? new MachineSystemInfo();
        _gate = gate ?? new ActionGateChain();
        _features = features ?? new OpenFeatureProbe();
        _isAdmin = isAdministrator ?? PrivilegeHelper.IsAdministrator;
    }

    public IConfirmationService? Confirmation { get; set; }

    internal async Task<ChangeResult> ExecuteAsync(IChangeAction action, bool isRevert = false, CancellationToken ct = default)
    {
        return await ExecuteCoreAsync(action, isRevert, new GateSession { Arm64Confirmed = Arm64Lease.IsConfirmed }, ct).ConfigureAwait(false);
    }

    private async Task<ChangeResult> ExecuteCoreAsync(
        IChangeAction action,
        bool isRevert,
        GateSession session,
        CancellationToken ct)
    {
        if (!isRevert
            && string.Equals(_settings.RestorePointPolicy, "BeforeIrreversible", StringComparison.OrdinalIgnoreCase)
            && !action.CanRevert
            && !await ConfirmMissingRestorePointAsync(ct).ConfigureAwait(false))
            return Journal(action, false, ChangeResult.FailCode(RestoreCode()));

        var antiCheatOk = false;
        while (true)
        {
            var decision = _gate.Decide(BuildContext(action, isRevert, session.Arm64Confirmed, antiCheatOk));
            if (decision.Kind == GateKind.Allow)
                break;
            if (decision.Kind == GateKind.Deny)
                return Journal(action, isRevert, ChangeResult.FailCode(decision.ErrorCode ?? GateCodes.Blocked));

            var accepted = Confirmation is not null &&
                           await Confirmation.ConfirmAsync(decision.ErrorCode ?? "", ct).ConfigureAwait(false);
            if (!accepted)
                return Journal(action, isRevert, ChangeResult.FailCode(decision.ErrorCode ?? GateCodes.AntiCheat));
            if (decision.ErrorCode == GateCodes.Arm64)
                session.Arm64Confirmed = true;
            if (decision.ErrorCode == GateCodes.AntiCheat)
                antiCheatOk = true;
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

        if (!isRevert && result.ErrorCode == "gate.irreversibleConfirm" && action is CommandChangeAction command)
        {
            if (!await ConfirmMissingRestorePointAsync(ct).ConfigureAwait(false))
                return Journal(action, false, ChangeResult.FailCode(RestoreCode()));
            command.AcceptIrreversible();
            try
            {
                result = await action.ApplyAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = ChangeResult.Fail(ex.Message);
            }
        }

        return Journal(action, isRevert, result);
    }

    private async Task<bool> ConfirmMissingRestorePointAsync(CancellationToken ct)
    {
        var outcome = _restore.CreateChecked("WinCleaner");
        if (outcome == RestorePointOutcome.Created)
            return true;
        var code = outcome == RestorePointOutcome.ProtectionDisabled ? "gate.protectionOff" : "gate.noNewPoint";
        return Confirmation is not null && await Confirmation.ConfirmAsync(code, ct).ConfigureAwait(false);
    }

    private static string RestoreCode() =>
        RestorePointManager.LastOutcome == RestorePointOutcome.ProtectionDisabled ? "gate.protectionOff" : "gate.noNewPoint";

    private ChangeResult Journal(IChangeAction action, bool isRevert, ChangeResult result)
    {
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
            ErrorCode = result.ErrorCode,
            Category = action.Category
        });

        if (isRevert && result.Success)
            _log.RemoveFromJournal(action.Id);

        return result;
    }

    private ActionGateContext BuildContext(IChangeAction action, bool isRevert, bool arm64Confirmed, bool antiCheatConfirmed = false)
    {
        var feature = action.Constraints.RequiresFeature;
        return new ActionGateContext
        {
            Action = action,
            IsRevert = isRevert,
            AllowDangerous = _settings.AllowDangerousActions,
            IsAdministrator = _isAdmin(),
            System = _system,
            DependencyAvailable = string.IsNullOrWhiteSpace(feature) || _features.IsAvailable(feature),
            Arm64Confirmed = arm64Confirmed,
            AntiCheatConfirmed = antiCheatConfirmed
        };
    }

    private sealed class GateSession
    {
        public bool Arm64Confirmed { get; set; }
    }

    internal async Task<IReadOnlyList<ChangeResult>> ExecuteManyAsync(
        IEnumerable<IChangeAction> actions,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        string? snapshotName = null)
    {
        var results = new List<ChangeResult>();
        var snapshotItems = new List<SnapshotItem>();
        var list = actions.ToList();
        if (list.Count > 0
            && string.Equals(_settings.RestorePointPolicy, "BeforeEachBatch", StringComparison.OrdinalIgnoreCase)
            && !await ConfirmMissingRestorePointAsync(ct).ConfigureAwait(false))
        {
            foreach (var action in list)
                results.Add(Journal(action, false, ChangeResult.FailCode(RestoreCode())));
            return results;
        }

        var session = new GateSession { Arm64Confirmed = Arm64Lease.IsConfirmed };
        if (list.Count > 0 && _system.OsArch == System.Runtime.InteropServices.Architecture.Arm64 && !session.Arm64Confirmed)
        {
            var accepted = Confirmation is not null &&
                           await Confirmation.ConfirmAsync(GateCodes.Arm64, ct).ConfigureAwait(false);
            if (!accepted)
            {
                foreach (var action in list)
                    results.Add(Journal(action, false, ChangeResult.FailCode(GateCodes.Arm64)));
                return results;
            }

            session.Arm64Confirmed = true;
        }

        foreach (var action in list)
        {
            ct.ThrowIfCancellationRequested();

            if (!action.IsOneShot)
            {
                try
                {
                    if (action.IsApplied())
                    {
                        results.Add(ChangeResult.Ok(null, null, "exec.alreadyApplied"));
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"IsApplied failed for {action.Id}: {ex.Message}");
                }
            }

            progress?.Report(action.DisplayName);
            var result = await ExecuteCoreAsync(action, false, session, ct).ConfigureAwait(false);
            results.Add(result);
            if (!result.Success)
                continue;

            snapshotItems.Add(new SnapshotItem
            {
                ActionId = action.Id,
                DisplayName = action.DisplayName,
                OldValue = result.OldValue,
                NewValue = result.NewValue,
                CanRevert = action.CanRevert,
                Risk = action.Risk.ToString()
            });
        }

        if (snapshotItems.Count > 0)
        {
            _snapshots.Append(
                snapshotName ?? $"Manual · {DateTime.Now:HH:mm}",
                snapshotItems,
                _restore.HasSessionCheckpoint);
        }

        return results;
    }
}

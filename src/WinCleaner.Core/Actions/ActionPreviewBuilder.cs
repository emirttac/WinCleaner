using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Actions;

public static class ActionPreviewBuilder
{
    public const string AbsentMarker = "(absent)";

    public static BatchPreview Build(IEnumerable<IChangeAction> actions)
    {
        var items = new List<BatchPreviewItem>();
        foreach (var action in actions)
        {
            ActionPreview preview;
            try
            {
                preview = action.BuildPreview();
            }
            catch
            {
                preview = ActionPreviewDefaults.FromAction(action, current: null, proposed: "applied");
            }

            items.Add(new BatchPreviewItem { Action = action, Preview = preview });
        }

        return new BatchPreview(items);
    }

    public static PreviewDiffRow RegistryRow(RegistryManager registry, RegistryTweakSpec spec)
    {
        var target = FormatRegistryTarget(spec);
        var current = ReadRegistryCurrent(registry, spec);
        string proposed;
        PreviewDiffKind kind;

        if (spec.DeleteKeyOnDisable &&
            spec.Path.Contains("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", StringComparison.OrdinalIgnoreCase))
        {
            proposed = "key-present";
            kind = PreviewDiffKind.Set;
        }
        else if (spec.DeleteValueOnEnable)
        {
            proposed = AbsentMarker;
            kind = PreviewDiffKind.Delete;
        }
        else if (spec.DeleteKeyOnDisable)
        {
            proposed = "key-present";
            kind = PreviewDiffKind.Set;
        }
        else
        {
            proposed = spec.EnabledValue;
            kind = PreviewDiffKind.Set;
        }

        var unchanged = ValuesMatch(current, proposed, spec.ValueKind);
        return new PreviewDiffRow
        {
            Target = target,
            Current = current ?? AbsentMarker,
            Proposed = proposed,
            Kind = kind,
            Unchanged = unchanged
        };
    }

    public static string FormatRegistryTarget(RegistryTweakSpec spec)
    {
        var name = string.IsNullOrEmpty(spec.Name) ? "(Default)" : spec.Name;
        if (spec.ApplyToAllSubkeys)
            return $"{spec.Hive}\\{spec.Path}\\*\\{name}";
        return $"{spec.Hive}\\{spec.Path}\\{name}";
    }

    private static string? ReadRegistryCurrent(RegistryManager registry, RegistryTweakSpec spec)
    {
        if (spec.DeleteKeyOnDisable &&
            spec.Path.Contains("{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", StringComparison.OrdinalIgnoreCase))
            return registry.IsClassicContextMenuEnabled() ? "key-present" : "key-absent";

        if (spec.DeleteKeyOnDisable)
            return registry.KeyExists(spec.Hive, spec.Path) ? "key-present" : "key-absent";

        if (spec.ApplyToAllSubkeys)
            return "per-interface";

        var value = registry.GetValueAsString(spec.Hive, spec.Path, spec.Name);
        return value;
    }

    private static bool ValuesMatch(string? current, string proposed, string valueKind)
    {
        if (proposed == AbsentMarker)
            return current is null;
        if (current is null)
            return false;
        if (proposed == "per-interface" || current == "per-interface")
            return false;
        return RegistryManager.ValuesEqual(current, proposed, valueKind);
    }
}

public static class ActionPreviewDefaults
{
    public static ActionPreview FromAction(
        IChangeAction action,
        string? current,
        string? proposed,
        PreviewDiffKind? kind = null,
        IReadOnlyList<PreviewDiffRow>? diffs = null)
    {
        bool applied;
        try { applied = action.IsApplied(); }
        catch { applied = false; }

        if (action.IsOneShot)
            applied = false;

        var resolvedKind = kind ?? (!action.CanRevert
            ? PreviewDiffKind.Irreversible
            : action.IsOneShot ? PreviewDiffKind.Script : PreviewDiffKind.Set);

        var rows = diffs ??
        [
            new PreviewDiffRow
            {
                Target = action.Id,
                Current = current,
                Proposed = proposed,
                Kind = resolvedKind,
                Unchanged = applied && string.Equals(current, proposed, StringComparison.OrdinalIgnoreCase)
            }
        ];

        if (diffs is null && applied && rows.Count == 1)
        {
            rows = [new PreviewDiffRow
            {
                Target = rows[0].Target,
                Current = current,
                Proposed = proposed,
                Kind = resolvedKind,
                Unchanged = true
            }];
        }

        return new ActionPreview
        {
            ActionId = action.Id,
            DisplayName = action.DisplayName,
            Description = action.Description,
            Risk = action.Risk,
            RequiresReboot = action.RequiresReboot,
            RequiresAntiCheatConfirm = action.RequiresAntiCheatConfirm,
            CanRevert = action.CanRevert,
            AlreadyApplied = applied,
            Diffs = rows
        };
    }
}

using WinCleaner.Core.Models;

namespace WinCleaner.Core.Actions;

public enum PreviewDiffKind
{
    Set,
    Delete,
    Script,
    Irreversible
}

public sealed class PreviewDiffRow
{
    public required string Target { get; init; }
    public string? Current { get; init; }
    public string? Proposed { get; init; }
    public PreviewDiffKind Kind { get; init; }
    public bool Unchanged { get; init; }
}

public sealed class ActionPreview
{
    public required string ActionId { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required RiskLevel Risk { get; init; }
    public bool RequiresReboot { get; init; }
    public bool RequiresAntiCheatConfirm { get; init; }
    public bool CanRevert { get; init; }
    public bool AlreadyApplied { get; init; }
    public IReadOnlyList<PreviewDiffRow> Diffs { get; init; } = [];
}

public sealed class BatchPreviewItem
{
    public required IChangeAction Action { get; init; }
    public required ActionPreview Preview { get; init; }
}

public sealed class BatchPreview
{
    public BatchPreview(IReadOnlyList<BatchPreviewItem> items)
    {
        Items = items;
    }

    public IReadOnlyList<BatchPreviewItem> Items { get; }

    public int TotalCount => Items.Count;
    public int AlreadyAppliedCount => Items.Count(i => i.Preview.AlreadyApplied);
    public int ChangeCount => Items.Count(i => !i.Preview.AlreadyApplied);
    public int IrreversibleCount => Items.Count(i => !i.Preview.CanRevert && !i.Preview.AlreadyApplied);
    public int RebootCount => Items.Count(i => i.Preview.RequiresReboot && !i.Preview.AlreadyApplied);
    public int AntiCheatCount => Items.Count(i => i.Preview.RequiresAntiCheatConfirm && !i.Preview.AlreadyApplied);
    public int SafeCount => CountRisk(RiskLevel.Safe);
    public int CautionCount => CountRisk(RiskLevel.Caution);
    public int DangerousCount => CountRisk(RiskLevel.Dangerous);
    public bool HasDangerous => DangerousCount > 0;
    public bool HasAntiCheat => AntiCheatCount > 0;

    public IReadOnlyList<IChangeAction> ToApply =>
        Items.Where(i => !i.Preview.AlreadyApplied).Select(i => i.Action).ToList();

    private int CountRisk(RiskLevel risk) =>
        Items.Count(i => !i.Preview.AlreadyApplied && i.Preview.Risk == risk);
}

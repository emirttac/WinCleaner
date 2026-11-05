using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Models;
using WinCleaner.Services;

namespace WinCleaner.Views;

public partial class ApplyPreviewDialog : Window
{
    private readonly bool _requiresAck;

    public ApplyPreviewDialog(BatchPreview batch, string title, LocalizationService loc)
    {
        InitializeComponent();
        Title = loc.Get("dlg.previewTitle");
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? loc.Get("dlg.previewTitle") : title;

        SummaryText.Text = loc.Format(
            "preview.summary",
            batch.ChangeCount,
            batch.AlreadyAppliedCount,
            batch.IrreversibleCount,
            batch.RebootCount,
            batch.AntiCheatCount);

        var rows = new ObservableCollection<PreviewActionRow>();
        foreach (var item in batch.Items)
            rows.Add(PreviewActionRow.From(item.Preview, loc));
        ItemList.ItemsSource = rows;

        _requiresAck = batch.HasDangerous || batch.HasAntiCheat;
        if (_requiresAck)
        {
            AckCheck.Visibility = Visibility.Visible;
            AckCheck.Content = batch.HasDangerous
                ? loc.Get("preview.ackDanger")
                : loc.Get("preview.ackAnticheat");
            ApplyButton.IsEnabled = false;
            AckCheck.Checked += (_, _) => ApplyButton.IsEnabled = true;
            AckCheck.Unchecked += (_, _) => ApplyButton.IsEnabled = false;
        }

        CancelButton.Content = loc.Get("common.cancel");
        ApplyButton.Content = loc.Format("preview.applyN", batch.ChangeCount);
        if (batch.ChangeCount == 0)
            ApplyButton.IsEnabled = false;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_requiresAck && AckCheck.IsChecked != true)
            return;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class PreviewActionRow
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string RiskLabel { get; init; }
    public required Brush RiskBrush { get; init; }
    public required Brush RiskTextBrush { get; init; }
    public required string Badge { get; init; }
    public required IReadOnlyList<PreviewDiffDisplay> Diffs { get; init; }

    public static PreviewActionRow From(ActionPreview preview, LocalizationService loc)
    {
        var riskKey = preview.Risk switch
        {
            RiskLevel.Blocked => "common.risk.blocked",
            RiskLevel.Dangerous => "common.risk.dangerous",
            RiskLevel.Caution => "common.risk.caution",
            _ => "common.risk.safe"
        };

        var badge = preview.AlreadyApplied
            ? loc.Get("preview.alreadyApplied")
            : !preview.CanRevert
                ? loc.Get("preview.irreversible")
                : preview.RequiresReboot
                    ? loc.Get("preview.reboot")
                    : "";

        var brushKey = preview.Risk switch
        {
            RiskLevel.Dangerous => "DangerBrush",
            RiskLevel.Caution => "WarningBrush",
            RiskLevel.Blocked => "TextMutedBrush",
            _ => "SuccessBrush"
        };
        var textKey = preview.Risk switch
        {
            RiskLevel.Dangerous => "BadgeOnDangerBrush",
            RiskLevel.Caution => "BadgeOnWarningBrush",
            RiskLevel.Blocked => "BadgeOnMutedBrush",
            _ => "BadgeOnSuccessBrush"
        };

        return new PreviewActionRow
        {
            Title = preview.DisplayName,
            Description = preview.Description,
            RiskLabel = loc.Get(riskKey),
            RiskBrush = Application.Current?.TryFindResource(brushKey) as Brush ?? Brushes.Gray,
            RiskTextBrush = Application.Current?.TryFindResource(textKey) as Brush ?? Brushes.Black,
            Badge = badge,
            Diffs = preview.Diffs.Select(d => new PreviewDiffDisplay
            {
                Target = d.Target,
                Current = $"{loc.Get("preview.current")}: {d.Current}",
                Proposed = $"{loc.Get("preview.proposed")}: {d.Proposed}",
                TargetBrush = d.Kind == PreviewDiffKind.Irreversible
                    ? (Application.Current?.TryFindResource("DangerBrush") as Brush ?? Brushes.OrangeRed)
                    : (Application.Current?.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray)
            }).ToList()
        };
    }
}

public sealed class PreviewDiffDisplay
{
    public required string Target { get; init; }
    public required string Current { get; init; }
    public required string Proposed { get; init; }
    public required Brush TargetBrush { get; init; }
}

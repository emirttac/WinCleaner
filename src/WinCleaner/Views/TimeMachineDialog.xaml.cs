using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WinCleaner.Core.Services;
using WinCleaner.Services;

namespace WinCleaner.Views;

public partial class TimeMachineDialog : Window
{
    private readonly LocalizationService _loc;
    private readonly Func<ChangeSnapshot, Task<bool>> _revertSnapshot;
    private readonly Func<ChangeSnapshot, SnapshotItem, Task<bool>> _revertItem;

    public TimeMachineDialog(
        IReadOnlyList<ChangeSnapshot> snapshots,
        LocalizationService loc,
        Func<ChangeSnapshot, Task<bool>> revertSnapshot,
        Func<ChangeSnapshot, SnapshotItem, Task<bool>> revertItem)
    {
        InitializeComponent();
        _loc = loc;
        _revertSnapshot = revertSnapshot;
        _revertItem = revertItem;

        Title = loc.Get("tm.title");
        TitleText.Text = loc.Get("tm.title");
        SubtitleText.Text = loc.Get("tm.subtitle");
        EmptyDetail.Text = loc.Get("tm.empty");
        RevertSnapshotButton.Content = loc.Get("tm.revertSnapshot");
        CloseButton.Content = loc.Get("common.cancel");

        SnapshotList.ItemsSource = snapshots.Select(s => new SnapshotListRow
        {
            Snapshot = s,
            Label = $"{s.CreatedAt:yyyy-MM-dd HH:mm} — {s.Name} ({s.Items.Count})"
        }).ToList();

        if (SnapshotList.Items.Count > 0)
            SnapshotList.SelectedIndex = 0;
        else
            RevertSnapshotButton.IsEnabled = false;
    }

    private ChangeSnapshot? SelectedSnapshot =>
        (SnapshotList.SelectedItem as SnapshotListRow)?.Snapshot;

    private void SnapshotList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var snap = SelectedSnapshot;
        if (snap is null)
        {
            ItemList.ItemsSource = null;
            EmptyDetail.Visibility = Visibility.Visible;
            RevertSnapshotButton.IsEnabled = false;
            return;
        }

        EmptyDetail.Visibility = Visibility.Collapsed;
        RevertSnapshotButton.IsEnabled = snap.RevertableCount > 0;
        ItemList.ItemsSource = snap.Items.Select(i => new SnapshotItemRow
        {
            DisplayName = i.DisplayName,
            Detail = $"{i.OldValue ?? "-"} → {i.NewValue ?? "-"}",
            CanRevert = i.CanRevert,
            RevertLabel = _loc.Get("tm.revertItem"),
            RevertCommand = new AsyncRelayCommand(async () =>
            {
                if (SelectedSnapshot is null) return;
                if (await _revertItem(SelectedSnapshot, i).ConfigureAwait(true))
                    DialogResult = true;
            })
        }).ToList();
    }

    private async void RevertSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var snap = SelectedSnapshot;
        if (snap is null) return;
        if (await _revertSnapshot(snap).ConfigureAwait(true))
            DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class SnapshotListRow
{
    public required ChangeSnapshot Snapshot { get; init; }
    public required string Label { get; init; }
}

public sealed class SnapshotItemRow
{
    public required string DisplayName { get; init; }
    public required string Detail { get; init; }
    public required bool CanRevert { get; init; }
    public required string RevertLabel { get; init; }
    public required ICommand RevertCommand { get; init; }
}

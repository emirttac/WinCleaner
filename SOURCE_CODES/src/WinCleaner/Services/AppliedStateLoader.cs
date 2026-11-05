using WinCleaner.Core.Services;
using WinCleaner.ViewModels;

namespace WinCleaner.Services;

internal static class AppliedStateLoader
{
    public static async Task LoadAsync(
        AppServices app,
        IReadOnlyList<TweakItemViewModel> items,
        int generation,
        Func<int> currentGeneration,
        Action completed)
    {
        var stamps = items.ToDictionary(item => item.Id, item => item.ProbeStamp, StringComparer.OrdinalIgnoreCase);
        var probes = items.Select(item => new AppliedProbe(item.Id, item.ReadAppliedFromSystem)).ToList();
        await app.AppliedState.ProbeAsync(probes, new WpfUiMarshal(), map =>
        {
            if (currentGeneration() != generation)
                return;

            foreach (var item in items)
            {
                if (!stamps.TryGetValue(item.Id, out var stamp) || item.ProbeStamp != stamp || item.IsBusy)
                    continue;
                if (map.TryGetValue(item.Id, out var applied))
                    item.SetAppliedSilent(applied);
            }

            completed();
        }).ConfigureAwait(false);
    }
}

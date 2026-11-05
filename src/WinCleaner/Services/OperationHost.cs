using WinCleaner.Core.Operations;
using WinCleaner.Views;

namespace WinCleaner.Services;

public static class OperationHost
{
    public static async Task<OperationResult> TrackAsync(
        IOperationRunner runner,
        string name,
        Func<CancellationToken, Task<OperationResult>> work)
    {
        var window = new OperationProgressWindow(name);
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
            window.Owner = owner;
        void OnStep(object? sender, OperationStep step) => window.Report(step);
        runner.StepReported += OnStep;
        window.Show();
        try
        {
            return await work(window.Token).ConfigureAwait(true);
        }
        finally
        {
            runner.StepReported -= OnStep;
            window.MarkCompleted();
        }
    }
}

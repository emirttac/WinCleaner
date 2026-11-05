using WinCleaner.Converters;
using WinCleaner.Core.Services;

namespace WinCleaner.Services;

public sealed class WpfUiMarshal : IUiMarshal
{
    public bool CheckAccess()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public void Invoke(Action action) => UiThread.Run(action);

    public void Post(Action action) => _ = UiThread.RunAsync(action);
}

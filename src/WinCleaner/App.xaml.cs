using System.Windows;
using System.Windows.Threading;
using WinCleaner.Services;

namespace WinCleaner;

public partial class App : Application
{
    private static int _uiErrorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Info("=== WinCleaner starting ===");
        AppLog.Info($"Log file: {AppLog.LogFilePath}");
        AppLog.Info($"OS: {Environment.OSVersion}, 64bit={Environment.Is64BitProcess}, CLR={Environment.Version}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"=== WinCleaner exiting code={e.ApplicationExitCode} ===");
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Crash("DispatcherUnhandledException", e.Exception);

        // Always mark handled so one binding glitch cannot kill the process.
        e.Handled = true;

        // Never spam MessageBox — list bindings can raise dozens of identical errors.
        var msg = e.Exception.Message ?? "";
        var isBinding =
            msg.Contains("TwoWay", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("OneWayToSource", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("Binding", StringComparison.OrdinalIgnoreCase) ||
            e.Exception is InvalidOperationException;

        if (isBinding)
            return;

        if (Interlocked.Exchange(ref _uiErrorDialogShown, 1) == 0)
        {
            try
            {
                string Loc(string key, string fallback) =>
                    Application.Current?.TryFindResource(key) as string ?? fallback;

                MessageBox.Show(
                    $"{Loc("msg.unexpectedError", "Unexpected error (details in the log):")}{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}{Loc("msg.logLabel", "Log:")}{Environment.NewLine}{AppLog.LogFilePath}",
                    "WinCleaner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }
        }
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Crash($"AppDomain(IsTerminating={e.IsTerminating})", ex);
        else
            AppLog.Error($"AppDomain non-Exception: {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Crash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }
}

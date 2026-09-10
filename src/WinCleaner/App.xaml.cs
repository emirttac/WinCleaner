using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WinCleaner.Core.Services;
using WinCleaner.Services;

namespace WinCleaner;

public partial class App : Application
{
    private static int _uiErrorDialogShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Info("=== WinCleaner starting ===");
        AppLog.Info($"Log file: {AppLog.LogFilePath}");
        AppLog.Info($"OS: {Environment.OSVersion}, 64bit={Environment.Is64BitProcess}, CLR={Environment.Version}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            if (!await EnsurePrerequisitesAsync().ConfigureAwait(true))
            {
                Shutdown(1);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Crash("PrerequisiteCheck", ex);
            MessageBox.Show(
                "Gereksinim kontrolü başarısız:\n" + ex.Message,
                "WinCleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            "Bu program daha geliştirilme ve test aşamasındadır. Bazı ayarlar çalışmayabilir. Lütfen çalışmayan fonksiyonları Github sayfasındaki Issues kısmında bildirin. Programı hep beraber geliştirelim <3",
            "WinCleaner",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static async Task<bool> EnsurePrerequisitesAsync()
    {
        var result = PrerequisiteChecker.Check(includeDotNetIfFrameworkDependent: true);
        if (!result.HasBlockingIssues)
            return true;

        var blocking = result.Issues.Where(i => !i.CanAutoInstall).ToList();
        if (blocking.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("WinCleaner açılamadı. Eksik / uyumsuz gereksinimler:");
            sb.AppendLine();
            foreach (var issue in blocking)
            {
                sb.AppendLine("• " + issue.Title);
                sb.AppendLine("  " + issue.Detail);
                sb.AppendLine();
            }

            MessageBox.Show(sb.ToString().TrimEnd(), "WinCleaner — Gereksinimler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var installable = result.Installable.ToList();
        var list = string.Join(Environment.NewLine, installable.Select(i => "• " + i.Title));
        var answer = MessageBox.Show(
            "Eksik bileşenler bulundu:" + Environment.NewLine + Environment.NewLine + list +
            Environment.NewLine + Environment.NewLine +
            "Şimdi indirilip sessizce kurulsun mu?",
            "WinCleaner — Gereksinimler",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes)
            return false;

        foreach (var issue in installable)
        {
            var progress = new Progress<string>(s => AppLog.Info(s));
            var ok = await PrerequisiteChecker.DownloadAndInstallAsync(issue, progress).ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(
                    issue.Title + " kurulumu tamamlanamadı.",
                    "WinCleaner",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
        }

        // Re-check — reboot may still be needed for VC++
        var again = PrerequisiteChecker.Check(includeDotNetIfFrameworkDependent: true);
        if (again.HasBlockingIssues)
        {
            MessageBox.Show(
                "Kurulum bitti ancak bazı gereksinimler hâlâ eksik görünüyor. Bilgisayarı yeniden başlatıp WinCleaner’ı tekrar aç.",
                "WinCleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
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

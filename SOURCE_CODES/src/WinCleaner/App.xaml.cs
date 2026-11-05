using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WinCleaner.Core.Actions;
using WinCleaner.Core.Services;
using WinCleaner.Data;
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

        var loc = new LocalizationService();
        try
        {
            loc.Initialize(AppSettings.Load().Language);
        }
        catch
        {
            loc.Initialize("tr");
        }

        if (ConfigArgs.TryParse(e.Args, out var config, out var configError))
        {
            if (config is null)
            {
                MessageBox.Show(configError ?? loc.Get("precheck.failed"), loc.Get("app.title"));
                Shutdown(2);
                return;
            }

            var known = CatalogLoader.LoadTweaks()
                .Select(t => t.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outcome = await ConfigSession.RunAsync(config, known, ApplyConfigTweaksAsync).ConfigureAwait(true);
            if (config.Silent || config.WhatIf || outcome.Rejected)
            {
                if (!config.Silent)
                    MessageBox.Show(outcome.Summary, loc.Get("app.title"));
                Shutdown(outcome.Rejected ? 2 : 0);
                return;
            }
        }

        try
        {
            if (!await EnsurePrerequisitesAsync(loc).ConfigureAwait(true))
            {
                Shutdown(1);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Crash("PrerequisiteCheck", ex);
            MessageBox.Show(
                loc.Get("precheck.failed") + Environment.NewLine + ex.Message,
                loc.Get("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MessageBox.Show(
            loc.Get("startup.betaNotice"),
            loc.Get("app.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static async Task<bool> EnsurePrerequisitesAsync(LocalizationService loc)
    {
        var result = PrerequisiteChecker.Check(includeDotNetIfFrameworkDependent: true);
        if (!result.HasBlockingIssues)
            return true;

        var blocking = result.Issues.Where(i => !i.CanAutoInstall).ToList();
        if (blocking.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(loc.Get("precheck.blockedIntro"));
            sb.AppendLine();
            foreach (var issue in blocking)
            {
                sb.AppendLine("• " + issue.Title);
                sb.AppendLine("  " + DetailOf(loc, issue));
                sb.AppendLine();
            }

            MessageBox.Show(sb.ToString().TrimEnd(), loc.Get("precheck.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var installable = result.Installable.ToList();
        var list = string.Join(Environment.NewLine, installable.Select(i => "• " + i.Title + Environment.NewLine + DetailOf(loc, i)));
        var answer = MessageBox.Show(
            loc.Get("precheck.installPrompt") + Environment.NewLine + Environment.NewLine + list,
            loc.Get("precheck.title"),
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
                    string.Format(loc.Get("precheck.installFailed"), issue.Title),
                    loc.Get("app.title"),
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
                loc.Get("precheck.stillMissing"),
                loc.Get("app.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private static string DetailOf(LocalizationService loc, PrerequisiteIssue issue)
    {
        if (issue.Kind != PrerequisiteKind.VcRedist)
            return issue.Detail;
        var text = loc.Get("precheck.vcredist.detail");
        return text == "precheck.vcredist.detail" ? PrerequisiteChecker.VcRedistDetail : text;
    }

    private static async Task ApplyConfigTweaksAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var loc = new LocalizationService();
        loc.Initialize(AppSettings.Load().Language);
        var app = new AppServices(key => loc.Get(key));
        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var actions = CatalogLoader.LoadTweaks()
            .Where(t => wanted.Contains(t.Id))
            .Select(t => app.ActionFactory.CreateFromTweak(t))
            .Where(a => a is not null)
            .Cast<IChangeAction>()
            .ToList();
        if (actions.Count == 0)
            return;
        await app.RunActionsAsync("config", actions, null, cancellationToken).ConfigureAwait(false);
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

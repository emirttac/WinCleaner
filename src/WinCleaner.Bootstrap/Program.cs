using System.Diagnostics;
using WinCleaner.Core.Services;

namespace WinCleaner.Bootstrap;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var appDir = AppContext.BaseDirectory;
        var target = ResolveAppExe(appDir);
        if (target is null)
        {
            MessageBox.Show(
                "Uygulama dosyası bulunamadı.\n\nBeklenen yollar:\n• app\\WinCleaner.exe\n• WinCleaner.App.exe\n\nLauncher ile app klasörünü birlikte tut.",
                "WinCleaner",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Force .NET Desktop check — bootstrap is self-contained; the app is framework-dependent.
        var result = PrerequisiteChecker.Check(includeDotNetIfFrameworkDependent: false);
        var issues = result.Issues.ToList();

        if (!PrerequisiteChecker.HasDotNet8Desktop())
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.DotNetDesktopRuntime,
                ".NET 8 Desktop Runtime missing",
                "WinCleaner needs the .NET 8 Desktop Runtime (x64). It can be downloaded and installed now.",
                PrerequisiteChecker.DotNetDesktopUrl,
                "windowsdesktop-runtime-8.0-win-x64.exe",
                CanAutoInstall: true));
        }

        if (!PrerequisiteChecker.IsVcRedistInstalled()
            && issues.All(i => i.Kind != PrerequisiteKind.VcRedist))
        {
            issues.Add(new PrerequisiteIssue(
                PrerequisiteKind.VcRedist,
                "Visual C++ Redistributable missing",
                "Microsoft Visual C++ 2015–2022 (x64) is required.",
                PrerequisiteChecker.VcRedistUrl,
                "vc_redist.x64.exe",
                CanAutoInstall: true));
        }

        // Re-check OS / admin from the first pass (already in issues)
        var blocking = issues.Where(i => !i.CanAutoInstall).ToList();
        if (blocking.Count > 0)
        {
            var text = string.Join("\n\n", blocking.Select(b => $"• {b.Title}\n{b.Detail}"));
            MessageBox.Show(text, "WinCleaner — Gereksinimler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var installable = issues.Where(i => i.CanAutoInstall).ToList();
        if (installable.Count > 0)
        {
            var list = string.Join("\n", installable.Select(i => $"• {i.Title}"));
            var answer = MessageBox.Show(
                "Eksik bileşenler bulundu:\n\n" + list +
                "\n\nŞimdi indirilip kurulsun mu?\n(Yönetici onayı istenebilir.)",
                "WinCleaner — Gereksinimler",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (answer != DialogResult.Yes)
                return;

            foreach (var issue in installable)
            {
                try
                {
                    var ok = PrerequisiteChecker.DownloadAndInstallAsync(issue).GetAwaiter().GetResult();
                    if (!ok)
                    {
                        MessageBox.Show(
                            $"{issue.Title} kurulumu tamamlanamadı (exit kodu başarısız).",
                            "WinCleaner",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"{issue.Title} indirilemedi/kurulamadı:\n{ex.Message}",
                        "WinCleaner",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }
            }

            // Verify after install
            if (!PrerequisiteChecker.HasDotNet8Desktop())
            {
                MessageBox.Show(
                    ".NET 8 Desktop Runtime hâlâ görünmüyor. Kurulumdan sonra bilgisayarı yeniden başlatıp tekrar dene.",
                    "WinCleaner",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = Path.GetDirectoryName(target)!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "WinCleaner başlatılamadı:\n" + ex.Message,
                "WinCleaner",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string? ResolveAppExe(string appDir)
    {
        string[] candidates =
        [
            Path.Combine(appDir, "WinCleaner.App.exe"),
            Path.Combine(appDir, "app", "WinCleaner.exe"),
            Path.Combine(appDir, "WinCleaner.App", "WinCleaner.exe")
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}

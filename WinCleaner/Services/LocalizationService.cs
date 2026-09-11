using System.Globalization;
using System.Windows;

namespace WinCleaner.Services;

public sealed class LocalizationService
{
    /// <summary>ISO language codes shipped with the app.</summary>
    public static readonly string[] SupportedLanguageCodes =
        ["tr", "en", "de", "es", "zh", "fr", "ro", "ru"];

    private ResourceDictionary? _current;
    private readonly Dictionary<string, string> _fallback = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage { get; private set; } = "tr";

    public event Action? LanguageChanged;

    public void Initialize(string language)
    {
        SeedFallbacks();
        SetLanguage(language);
    }

    public static string NormalizeLanguageCode(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "tr";

        var code = language.Trim().ToLowerInvariant();
        // Accept BCP-47 prefixes (zh-CN -> zh, pt-BR unsupported -> tr fallback)
        var dash = code.IndexOf('-');
        if (dash > 0)
            code = code[..dash];

        return SupportedLanguageCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
            ? code
            : "tr";
    }

    public void SetLanguage(string language)
    {
        CurrentLanguage = NormalizeLanguageCode(language);
        var dict = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{CurrentLanguage}.xaml", UriKind.Relative)
        };

        var app = Application.Current;
        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var existing = app.Resources.MergedDictionaries[i];
            var src = existing.Source?.OriginalString ?? "";
            if (src.Contains("Strings.", StringComparison.OrdinalIgnoreCase))
                app.Resources.MergedDictionaries.RemoveAt(i);
        }

        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        LanguageChanged?.Invoke();
    }

    public string Get(string key)
    {
        if (_current is not null && _current.Contains(key))
            return _current[key]?.ToString() ?? key;

        if (Application.Current?.TryFindResource(key) is string s)
            return s;

        return _fallback.TryGetValue(key, out var fb) ? fb : key;
    }

    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    public string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", v, units[i]);
    }

    private void SeedFallbacks()
    {
        void A(string k, string v) => _fallback[k] = v;
        A("app.title", "WinCleaner");
        A("nav.dashboard", "Dashboard");
        A("nav.services", "Services");
        A("nav.telemetry", "Telemetry");
        A("nav.bloatware", "Bloatware");
        A("nav.gaming", "Gaming");
        A("nav.visual", "Visual");
        A("nav.win11", "Windows 11 & UI");
        A("nav.network", "Network");
        A("nav.tools", "Tools");
        A("nav.installer", "Package Installer");
        A("nav.presets", "Presets");
        A("nav.settings", "Settings");
        A("common.search", "Search settings...");
        A("common.apply", "Apply");
        A("common.undo", "Undo All");
        A("common.refresh", "Refresh");
        A("common.preview", "Preview");
        A("common.confirm", "Confirm");
        A("common.cancel", "Cancel");
        A("common.risk.safe", "Safe");
        A("common.risk.caution", "Caution");
        A("common.risk.dangerous", "Dangerous");
        A("common.risk.blocked", "Blocked");
        A("common.adminRequired", "This change requires Administrator privileges. Restart WinCleaner with Run as administrator.");
        A("common.rebootRequired", "Change saved. Restart your PC for it to take full effect.");
        A("msg.done", "Done");
        A("msg.failed", "Failed");
        A("msg.oneShotOk", "Command completed.");
        A("common.loading", "Loading...");
        A("danger.needSettings", "Turn on Allow dangerous actions in Settings before this operation.");
        A("tools.cleanTemp.confirm", "Delete files in Windows and user Temp folders?");
        A("dash.recentEmpty", "No changes in this session yet.");
        A("dash.createRestore", "Create Restore Point");
        A("restore.warnTitle", "No restore point yet");
        A("restore.warnBody", "You have not created a restore point in this session. A restore point will be created automatically before continuing.");
        A("restore.creating", "Creating restore point…");
        A("restore.created", "Restore point created.");
        A("restore.failed", "Could not create a restore point.");
        A("msg.unexpectedError", "Unexpected error (details in the log):");
        A("msg.logLabel", "Log:");
    }
}

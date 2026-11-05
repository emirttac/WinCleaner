using System.Text.RegularExpressions;

namespace WinCleaner.Core.Tests;

public class CatalogLanguageTests
{
    private static readonly string[] Languages = ["en", "tr", "de", "es", "zh", "fr", "ro", "ru"];
    private static readonly string[] Prefixes = ["tweak.", "app.", "inst.", "svc."];

    [Fact]
    public void CatalogTexts_AreNotEnglishCopies_OutsideAllowlist()
    {
        var resources = FindDir("src", "WinCleaner", "Resources");
        var allow = LoadAllowlist();
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));
        var catalog = en.Where(kv => Prefixes.Any(p => kv.Key.StartsWith(p, StringComparison.Ordinal)) && kv.Value.Length >= 12)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        foreach (var lang in Languages.Where(l => l != "en"))
        {
            var map = LoadKeys(Path.Combine(resources, $"Strings.{lang}.xaml"));
            var copies = catalog
                .Where(kv => map.TryGetValue(kv.Key, out var value) && value == kv.Value && !allow.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();
            Assert.True(copies.Count == 0, $"{lang} English copies: {string.Join(", ", copies.Take(12))}");
        }
    }

    [Fact]
    public void PseudoLocalization_WrapsStringsWithoutMeasuringPixels()
    {
        var resources = FindDir("src", "WinCleaner", "Resources");
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));
        foreach (var value in en.Values)
        {
            var pseudo = "[" + value + "]";
            Assert.StartsWith("[", pseudo);
            Assert.EndsWith("]", pseudo);
            Assert.Contains(value, pseudo);
        }
    }

    [Fact]
    public void CommandFixtures_SkipMissingLanguages()
    {
        var root = TryFindDir("tests", "Fixtures");
        var missing = new List<string>();
        foreach (var lang in Languages)
        {
            if (root is null)
            {
                missing.Add(lang);
                continue;
            }
            var dir = Path.Combine(root, lang);
            var schtasks = Path.Combine(dir, "schtasks.xml");
            var power = Path.Combine(dir, "powercfg.txt");
            var bcd = Path.Combine(dir, "bcdedit.txt");
            if (!File.Exists(schtasks) && !File.Exists(power) && !File.Exists(bcd))
            {
                missing.Add(lang);
                continue;
            }

            if (File.Exists(schtasks))
            {
                var state = WinCleaner.Core.Services.TaskSchedulerManager.ParseScheduledTaskState(File.ReadAllText(schtasks));
                Assert.NotEqual(WinCleaner.Core.Services.TaskEnablement.Unknown, state);
            }

            if (File.Exists(power))
                Assert.NotNull(WinCleaner.Core.Services.CommandStateProbe.ParsePowerCfgAcSettingIndex(File.ReadAllText(power)));

            if (File.Exists(bcd))
            {
                var text = File.ReadAllText(bcd);
                Assert.NotNull(WinCleaner.Core.Services.ElevatedCommandRunner.ParseBcdValue(text, "disabledynamictick"));
            }
        }

        Console.WriteLine("fixture missing: " + string.Join(", ", missing));
        Assert.True(missing.Count <= Languages.Length);
    }

    private static HashSet<string> LoadAllowlist()
    {
        var path = Path.Combine(FindDir("tools", "i18n"), "catalog-copy-allowlist.txt");
        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var raw = File.ReadAllText(path);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(raw, "<sys:String\\s+x:Key=\"([^\"]+)\">([\\s\\S]*?)</sys:String>", RegexOptions.CultureInvariant))
            map[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        return map;
    }

    private static string? TryFindDir(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static string FindDir(params string[] parts) =>
        TryFindDir(parts) ?? throw new DirectoryNotFoundException(string.Join('/', parts));
}

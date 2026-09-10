using System.Text.RegularExpressions;
using System.Xml.Linq;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class LocalizationParityTests
{
    private static readonly string[] Languages = ["en", "tr", "de", "es", "zh", "fr", "ro", "ru"];

    private static string FindResourcesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "WinCleaner", "Resources");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Strings.en.xaml")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/WinCleaner/Resources from test base directory.");
    }

    private static Dictionary<string, string> LoadKeys(string path)
    {
        var raw = File.ReadAllText(path);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(raw, "<sys:String\\s+x:Key=\"([^\"]+)\">([\\s\\S]*?)</sys:String>", RegexOptions.CultureInvariant))
        {
            map[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        Assert.NotEmpty(map);
        return map;
    }

    [Fact]
    public void AllLanguageFiles_HaveIdenticalKeySets_AndNonEmptyValues()
    {
        var resources = FindResourcesDir();
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));

        foreach (var lang in Languages)
        {
            var path = Path.Combine(resources, $"Strings.{lang}.xaml");
            Assert.True(File.Exists(path), $"Missing Strings.{lang}.xaml");
            var map = LoadKeys(path);

            var missing = en.Keys.Except(map.Keys).OrderBy(k => k).ToList();
            var extra = map.Keys.Except(en.Keys).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"{lang} missing keys: {string.Join(", ", missing.Take(10))}");
            Assert.True(extra.Count == 0, $"{lang} extra keys: {string.Join(", ", extra.Take(10))}");

            var empty = map.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).Take(10).ToList();
            Assert.True(empty.Count == 0, $"{lang} empty values: {string.Join(", ", empty)}");
        }

        Assert.Equal(529, en.Count);
    }

    [Fact]
    public void CatalogApps_HaveEnglishNameAndDescriptionKeys()
    {
        var resources = FindResourcesDir();
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));
        foreach (var app in CatalogLoader.LoadApps())
        {
            Assert.True(en.ContainsKey(app.DisplayNameKey), $"Missing {app.DisplayNameKey} for {app.Id}");
            Assert.True(en.ContainsKey(app.DescriptionKey), $"Missing {app.DescriptionKey} for {app.Id}");
        }
    }

    [Fact]
    public void CatalogTweaksServicesPresetsInstallers_HaveEnglishKeys()
    {
        var resources = FindResourcesDir();
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));

        foreach (var t in CatalogLoader.LoadTweaks())
        {
            Assert.True(en.ContainsKey(t.DisplayNameKey), $"Missing {t.DisplayNameKey} for {t.Id}");
            Assert.True(en.ContainsKey(t.DescriptionKey), $"Missing {t.DescriptionKey} for {t.Id}");
        }

        foreach (var s in CatalogLoader.LoadServices())
        {
            Assert.True(en.ContainsKey(s.DisplayNameKey), $"Missing {s.DisplayNameKey} for {s.Id}");
            Assert.True(en.ContainsKey(s.DescriptionKey), $"Missing {s.DescriptionKey} for {s.Id}");
        }

        foreach (var p in CatalogLoader.LoadPresets())
        {
            Assert.True(en.ContainsKey(p.DisplayNameKey), $"Missing {p.DisplayNameKey} for {p.Id}");
            Assert.True(en.ContainsKey(p.DescriptionKey), $"Missing {p.DescriptionKey} for {p.Id}");
        }

        foreach (var i in CatalogLoader.LoadInstallerApps())
        {
            Assert.True(en.ContainsKey(i.DisplayNameKey), $"Missing {i.DisplayNameKey} for {i.Id}");
            Assert.True(en.ContainsKey(i.DescriptionKey), $"Missing {i.DescriptionKey} for {i.Id}");
            if (!string.IsNullOrWhiteSpace(i.CategoryKey))
                Assert.True(en.ContainsKey(i.CategoryKey), $"Missing {i.CategoryKey} for {i.Id}");
        }
    }

    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("zh")]
    [InlineData("fr")]
    [InlineData("ro")]
    [InlineData("ru")]
    public void TranslatedFiles_DifferFromEnglish_OnSampleUiKeys(string lang)
    {
        var resources = FindResourcesDir();
        var en = LoadKeys(Path.Combine(resources, "Strings.en.xaml"));
        var other = LoadKeys(Path.Combine(resources, $"Strings.{lang}.xaml"));

        string[] sample =
        [
            "nav.settings",
            "common.apply",
            "common.cancel",
            "set.language",
            "danger.title",
            "msg.done"
        ];

        var identical = sample.Where(k =>
            en.TryGetValue(k, out var a) &&
            other.TryGetValue(k, out var b) &&
            string.Equals(a, b, StringComparison.Ordinal)).ToList();

        Assert.True(identical.Count == 0,
            $"{lang} still equals English for: {string.Join(", ", identical)}");
    }
}

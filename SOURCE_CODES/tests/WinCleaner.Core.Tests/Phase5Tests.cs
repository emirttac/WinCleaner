using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using WinCleaner.Core.Services;

namespace WinCleaner.Core.Tests;

public class Phase5Tests
{
    [Fact]
    public async Task StartupProbe_RejectsSynchronousUiMarshal()
    {
        var marshal = new RejectingSyncMarshal();
        var cache = new AppliedStateCache();
        var called = 0;

        var result = await cache.ProbeAsync(
            [new AppliedProbe("core-parking", () =>
            {
                called++;
                return true;
            })],
            marshal,
            map => Assert.True(map["core-parking"]),
            CancellationToken.None);

        Assert.Equal(0, marshal.SyncInvokeCount);
        Assert.Equal(1, called);
        Assert.True(result["core-parking"]);
        Assert.True(cache.TryGet("core-parking", out var cached) && cached);
    }

    [Fact]
    public void MissingTheme_StaysDark()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.Equal("Dark", settings!.Theme);
    }

    [Theory]
    [InlineData("DarkRed.xaml")]
    [InlineData("Light.xaml")]
    public void ThemeTokens_MeetContrastFloors(string fileName)
    {
        var colors = LoadColors(fileName);

        AssertText(colors, "TextPrimaryColor", "BgDeepColor");
        AssertText(colors, "TextPrimaryColor", "BgPanelColor");
        AssertText(colors, "TextPrimaryColor", "BgElevatedColor");
        AssertText(colors, "TextSecondaryColor", "BgDeepColor");
        AssertText(colors, "TextSecondaryColor", "BgPanelColor");
        AssertText(colors, "TextSecondaryColor", "BgElevatedColor");
        AssertText(colors, "TextMutedColor", "BgDeepColor");
        AssertText(colors, "TextMutedColor", "BgPanelColor");
        AssertText(colors, "TextMutedColor", "BgElevatedColor");

        AssertGraphic(colors, "BorderColor", "BgDeepColor");
        AssertGraphic(colors, "BorderColor", "BgPanelColor");
        AssertGraphic(colors, "BorderColor", "BgElevatedColor");
        AssertGraphic(colors, "SuccessColor", "BgDeepColor");
        AssertGraphic(colors, "SuccessColor", "BgPanelColor");
        AssertGraphic(colors, "SuccessColor", "BgElevatedColor");
        AssertGraphic(colors, "WarningColor", "BgDeepColor");
        AssertGraphic(colors, "WarningColor", "BgPanelColor");
        AssertGraphic(colors, "WarningColor", "BgElevatedColor");
        AssertGraphic(colors, "DangerColor", "BgDeepColor");
        AssertGraphic(colors, "DangerColor", "BgPanelColor");
        AssertGraphic(colors, "DangerColor", "BgElevatedColor");
        AssertGraphic(colors, "FocusColor", "BgDeepColor");
        AssertGraphic(colors, "FocusColor", "BgPanelColor");
        AssertGraphic(colors, "FocusColor", "BgElevatedColor");

        AssertText(colors, "BadgeOnSuccessColor", "SuccessColor");
        AssertText(colors, "BadgeOnWarningColor", "WarningColor");
        AssertText(colors, "BadgeOnDangerColor", "DangerColor");
        AssertText(colors, "BadgeOnMutedColor", "TextMutedColor");
        AssertText(colors, "OnAccentColor", "AccentColor");
    }

    private static void AssertText(IReadOnlyDictionary<string, string> colors, string foreground, string background) =>
        Assert.True(ContrastRatio(colors[foreground], colors[background]) >= 4.5,
            $"{foreground} on {background} is {ContrastRatio(colors[foreground], colors[background]):0.00}:1");

    private static void AssertGraphic(IReadOnlyDictionary<string, string> colors, string foreground, string background) =>
        Assert.True(ContrastRatio(colors[foreground], colors[background]) >= 3.0,
            $"{foreground} on {background} is {ContrastRatio(colors[foreground], colors[background]):0.00}:1");

    internal static double ContrastRatio(string foreground, string background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        var r = Channel(value[..2]);
        var g = Channel(value.Substring(2, 2));
        var b = Channel(value.Substring(4, 2));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Channel(string hex)
    {
        var srgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static Dictionary<string, string> LoadColors(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "WinCleaner", "Themes", fileName);
            if (File.Exists(candidate))
            {
                var doc = XDocument.Load(candidate);
                return doc.Descendants()
                    .Where(e => e.Name.LocalName == "Color" && e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) is not null)
                    .ToDictionary(
                        e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value,
                        e => e.Value.Trim(),
                        StringComparer.Ordinal);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(fileName);
    }

    private sealed class RejectingSyncMarshal : IUiMarshal
    {
        public int SyncInvokeCount { get; private set; }

        public bool CheckAccess() => false;

        public void Invoke(Action action)
        {
            SyncInvokeCount++;
            throw new InvalidOperationException("Synchronous UI marshal is rejected.");
        }

        public void Post(Action action) => action();
    }
}

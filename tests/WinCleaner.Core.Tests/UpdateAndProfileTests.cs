using System.Text.Json;
using WinCleaner.Core.Services;
using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class UpdateServiceVersionTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v2.0.0-beta", "2.0.0")]
    public void NormalizeVersion_StripsPrefixAndSuffix(string input, string expected)
    {
        Assert.Equal(expected, UpdateService.NormalizeVersion(input));
    }

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.1.0", false)]
    public void IsNewer_ComparesSemVer(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(
            UpdateService.NormalizeVersion(latest),
            UpdateService.NormalizeVersion(current)));
    }
}

public class CustomProfileDocumentTests
{
    [Fact]
    public void RoundTrip_PreservesTweaksAndServiceStates()
    {
        var profile = new CustomProfileDocument
        {
            SchemaVersion = 2,
            Id = "custom-test",
            Name = "TestProfile",
            Description = "unit test",
            TweakIds = ["tweak-game-mode", "tweak-qos", "tweak-visual-perf"],
            Services =
            [
                new ServiceStateEntry { Id = "svc-diagtrack", StartType = "Disabled" },
                new ServiceStateEntry { Id = "svc-wer", StartType = "Manual" }
            ]
        };

        var json = JsonSerializer.Serialize(profile);
        var back = JsonSerializer.Deserialize<CustomProfileDocument>(json);

        Assert.NotNull(back);
        Assert.Equal(2, back.SchemaVersion);
        Assert.Equal("TestProfile", back.Name);
        Assert.Equal(3, back.TweakIds.Count);
        Assert.Contains("tweak-qos", back.TweakIds);
        Assert.Equal(2, back.Services.Count);
        Assert.Contains(back.Services, s => s.Id == "svc-diagtrack" && s.StartType == "Disabled");
    }

    [Fact]
    public void LegacyFields_ResolveDisplayName()
    {
        var profile = new CustomProfileDocument
        {
            DisplayNameKey = "LegacyName",
            ServiceIds = ["svc-diagtrack"]
        };
        Assert.Equal("LegacyName", profile.ResolveDisplayName());
    }
}

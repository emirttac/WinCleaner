using WinCleaner.Data;

namespace WinCleaner.Core.Tests;

public class AppCatalogMatchingTests
{
    [Theory]
    [InlineData("Microsoft.People", "Microsoft.People", true)]
    [InlineData("Microsoft.People", "Microsoft.PeopleExperienceHost", false)]
    [InlineData("Microsoft.People", "Microsoft.Windows.PeopleExperienceHost", false)]
    [InlineData("Microsoft.MSPaint", "Microsoft.Paint", false)]
    [InlineData("Microsoft.MSPaint", "Microsoft.MSPaint", true)]
    [InlineData("Microsoft.XboxApp", "Microsoft.GamingApp", false)]
    [InlineData("MicrosoftTeams", "MSTeams", false)]
    [InlineData("Microsoft.Windows.Photos", "Microsoft.Windows.Photos", true)]
    [InlineData("Microsoft.Windows.Photos", "Microsoft.Windows.Photos.MediaFileTranscoder", true)]
    [InlineData("Clipchamp.Clipchamp", "Clipchamp.Clipchamp", true)]
    public void NameMatchesPattern_UsesBoundedPrefix(string pattern, string installed, bool expected)
    {
        Assert.Equal(expected, AppCatalogEntry.NameMatchesPattern(installed, pattern));
    }

    [Fact]
    public void PeopleEntry_DoesNotMatchPeopleExperienceHost()
    {
        var people = Assert.Single(CatalogLoader.LoadApps(), a => a.Id == "app-people");
        Assert.False(people.MatchesInstalledName("Microsoft.Windows.PeopleExperienceHost"));
        Assert.False(people.MatchesInstalledName("Microsoft.PeopleExperienceHost"));
        Assert.True(people.MatchesInstalledName("Microsoft.People"));
    }

    [Fact]
    public void Win11Snapshot_DetectsRenamedInboxApps()
    {
        // Package names captured on Windows 11 24H2 (build 26100).
        string[] installed =
        [
            "Microsoft.GamingApp",
            "MSTeams",
            "Microsoft.Copilot",
            "Microsoft.BingSearch",
            "Microsoft.OutlookForWindows",
            "Microsoft.XboxGamingOverlay",
            "Microsoft.YourPhone",
            "Microsoft.Windows.PeopleExperienceHost",
            "Microsoft.Paint",
            "Microsoft.WindowsStore"
        ];

        var apps = CatalogLoader.LoadApps().ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        Assert.True(apps["app-xboxapp"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-teams"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-copilot"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-bingsearch"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-outlook"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-xboxgamingoverlay"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-yourphone"].MatchesAnyInstalled(installed));
        Assert.True(apps["app-store"].MatchesAnyInstalled(installed));
        Assert.False(apps["app-people"].MatchesAnyInstalled(installed));
        Assert.False(apps["app-paint3d"].MatchesAnyInstalled(installed));
        Assert.False(apps["app-xboxapp"].MatchesInstalledName("Microsoft.XboxGamingOverlay"));
    }
}

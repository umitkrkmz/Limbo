using Limbo.Console;

namespace Limbo.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("V2.0", "2.0.0")]
    [InlineData("1.10.3", "1.10.3")]
    [InlineData("v1.2.0-rc.1", "1.2.0")]
    [InlineData("v1.2.0+build.5", "1.2.0")]
    public void TryParseTag_ParsesReleaseTags(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseTag(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void TryParseTag_RejectsGarbage(string tag)
    {
        Assert.False(UpdateChecker.TryParseTag(tag, out _));
    }

    [Theory]
    [InlineData("v1.10.0", "1.9.0", true)]   // numeric, not string, comparison
    [InlineData("v1.1.1", "1.1.0", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("v1.1.0", "1.1.0", false)]
    [InlineData("v1.1.0", "1.1.0.0", false)] // assembly versions carry a 4th part
    [InlineData("v1.0.9", "1.1.0", false)]
    [InlineData("garbage", "1.0.0", false)]
    public void IsNewer_ComparesVersionsNumerically(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewer(tag, Version.Parse(current)));
    }

    // Hits the real GitHub API; must tolerate no network / rate limiting.
    [Fact]
    public async Task CheckAsync_AgainstRealRepo_ReportsOnlyNewerReleases()
    {
        var ancient = await UpdateChecker.CheckAsync(AppInfo.LatestReleaseApiUrl, new Version(0, 0, 1));
        if (ancient.Succeeded)
        {
            Assert.NotNull(ancient.Update);
            Assert.StartsWith("v", ancient.Update!.LatestTag);
            Assert.StartsWith(AppInfo.RepoUrl + "/releases/", ancient.Update.ReleaseUrl);
        }

        var future = await UpdateChecker.CheckAsync(AppInfo.LatestReleaseApiUrl, new Version(999, 0, 0));
        if (future.Succeeded)
        {
            Assert.Null(future.Update);
        }
    }
}

using OverlayTranslate.Services;

namespace OverlayTranslate.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.0", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("v1.2.3", "1.3.0", false)]
    [InlineData("1.2.3", "1.2.0", true)]
    public void ParseVersion_CompareVersions_WorksCorrectly(string tag, string current, bool shouldUpdate)
    {
        var tagVersion = Version.Parse(tag.TrimStart('v'));
        var currentVersion = Version.Parse(current);

        Assert.Equal(shouldUpdate, tagVersion > currentVersion);
    }

    [Fact]
    public void GitHubRelease_DefaultValues_AreCorrect()
    {
        var release = new GitHubRelease();

        Assert.Equal("", release.TagName);
        Assert.Equal("", release.Name);
        Assert.Equal("", release.Body);
        Assert.Empty(release.Assets);
    }

    [Fact]
    public void GitHubAsset_DefaultValues_AreCorrect()
    {
        var asset = new GitHubAsset();

        Assert.Equal("", asset.Name);
        Assert.Equal("", asset.BrowserDownloadUrl);
        Assert.Equal(0, asset.Size);
    }
}
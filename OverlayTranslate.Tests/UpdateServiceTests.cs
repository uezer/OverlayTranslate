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

    [Fact]
    public void UpdateCheckResult_UpToDate_HasCorrectKind()
    {
        var result = UpdateCheckResult.UpToDate(new Version(1, 0, 0));

        Assert.Equal(UpdateCheckResultKind.UpToDate, result.Kind);
        Assert.Equal(new Version(1, 0, 0), result.CurrentVersion);
        Assert.Null(result.Release);
    }

    [Fact]
    public void UpdateCheckResult_Failure_HasCorrectKind()
    {
        var result = UpdateCheckResult.Failure(new Version(1, 0, 0), "网络错误");

        Assert.Equal(UpdateCheckResultKind.Failed, result.Kind);
        Assert.Equal("网络错误", result.ErrorMessage);
    }

    [Fact]
    public void UpdateCheckResult_Available_HasCorrectKind()
    {
        var release = new GitHubRelease { TagName = "v2.0.0" };
        var result = UpdateCheckResult.Available(release, new Version(2, 0, 0), new Version(1, 0, 0));

        Assert.Equal(UpdateCheckResultKind.UpdateAvailable, result.Kind);
        Assert.Same(release, result.Release);
        Assert.Equal(new Version(2, 0, 0), result.LatestVersion);
    }
}
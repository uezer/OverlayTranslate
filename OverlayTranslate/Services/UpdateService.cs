using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Serilog;

namespace OverlayTranslate.Services;

public class UpdateService
{
    private const string RepoOwner = "uezer";
    private const string RepoName = "OverlayTranslate";

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OverlayTranslate-Updater");
        }
    }

    /// <summary>
    /// 检查是否有新版本
    /// </summary>
    public async Task<GitHubRelease?> CheckForUpdateAsync(CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetFromJsonAsync<GitHubRelease>(url, ct);

            if (response == null) return null;

            var latestVersion = ParseVersion(response.TagName);
            var currentVersion = GetCurrentVersion();

            Log.Information("版本检查: 当前={Current}, 最新={Latest}", currentVersion, latestVersion);

            return latestVersion > currentVersion ? response : null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "检查更新失败");
            return null;
        }
    }

    /// <summary>
    /// 下载安装包
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        GitHubRelease release,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));
        if (asset == null)
            throw new InvalidOperationException("未找到安装包");

        var tempPath = Path.Combine(Path.GetTempPath(), asset.Name);

        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = new FileStream(tempPath, FileMode.Create);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            if (totalBytes > 0)
                progress.Report((double)totalRead / totalBytes * 100);
        }

        Log.Information("下载完成: {Path} ({Size} bytes)", tempPath, totalRead);
        return tempPath;
    }

    /// <summary>
    /// 启动安装程序并退出当前应用
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath)
    {
        Log.Information("启动安装程序: {Path}", installerPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });

        Application.Current.Shutdown();
    }

    internal static Version ParseVersion(string tag)
    {
        var versionStr = tag.TrimStart('v');
        return Version.TryParse(versionStr, out var v) ? v : new Version(0, 0, 0);
    }

    internal static Version GetCurrentVersion()
    {
        return typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);
    }
}
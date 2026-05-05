using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Serilog;

namespace OverlayTranslate.Services;

/// <summary>
/// 更新检查结果
/// </summary>
public enum UpdateCheckResultKind
{
    /// <summary>有可用更新</summary>
    UpdateAvailable,
    /// <summary>已是最新版本</summary>
    UpToDate,
    /// <summary>检查失败（网络错误等）</summary>
    Failed
}

/// <summary>
/// 更新检查结果
/// </summary>
public class UpdateCheckResult
{
    public UpdateCheckResultKind Kind { get; init; }
    public GitHubRelease? Release { get; init; }
    public Version? LatestVersion { get; init; }
    public Version CurrentVersion { get; init; } = null!;
    public string? ErrorMessage { get; init; }

    public static UpdateCheckResult Available(GitHubRelease release, Version latest, Version current)
        => new() { Kind = UpdateCheckResultKind.UpdateAvailable, Release = release, LatestVersion = latest, CurrentVersion = current };

    public static UpdateCheckResult UpToDate(Version current)
        => new() { Kind = UpdateCheckResultKind.UpToDate, CurrentVersion = current };

    public static UpdateCheckResult Failure(Version current, string error)
        => new() { Kind = UpdateCheckResultKind.Failed, CurrentVersion = current, ErrorMessage = error };
}

/// <summary>
/// 统一的更新服务——负责检查更新、下载安装包、启动安装。
/// 通过 DI 注册为 Singleton，内置速率限制。
/// </summary>
public class UpdateService
{
    private const string RepoOwner = "uezer";
    private const string RepoName = "OverlayTranslate";
    private static readonly TimeSpan RateLimitInterval = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly object _cacheLock = new();
    private UpdateCheckResult? _cachedResult;
    private DateTime _lastCheckTime = DateTime.MinValue;

    public UpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OverlayTranslate-Updater");
        }
    }

    /// <summary>
    /// 检查更新。默认遵守速率限制（1 小时内返回缓存），传 bypassRateLimit=true 可强制请求。
    /// 内置 15 秒超时，避免长时间卡住。
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        CancellationToken ct, bool bypassRateLimit = false)
    {
        var currentVersion = GetCurrentVersion();

        // 速率限制：1 小时内直接返回缓存结果
        if (!bypassRateLimit)
        {
            lock (_cacheLock)
            {
                if (_cachedResult != null && DateTime.UtcNow - _lastCheckTime < RateLimitInterval)
                {
                    Log.Debug("使用缓存的更新检查结果（上次检查: {Time}）", _lastCheckTime);
                    return _cachedResult;
                }
            }
        }

        try
        {
            // 15 秒超时，防止网络问题长时间阻塞
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(url, timeoutCts.Token);

            if (release == null)
            {
                var result = UpdateCheckResult.UpToDate(currentVersion);
                CacheResult(result);
                return result;
            }

            var latestVersion = ParseVersion(release.TagName);
            Log.Information("版本检查: 当前={Current}, 最新={Latest}", currentVersion, latestVersion);

            if (latestVersion > currentVersion)
            {
                var result = UpdateCheckResult.Available(release, latestVersion, currentVersion);
                CacheResult(result);
                return result;
            }

            var upToDate = UpdateCheckResult.UpToDate(currentVersion);
            CacheResult(upToDate);
            return upToDate;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 超时（非用户取消）
            Log.Warning("检查更新超时（15 秒）");
            var result = UpdateCheckResult.Failure(currentVersion, "请求超时，请检查网络连接或代理设置");
            CacheResult(result);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "检查更新网络请求失败: {StatusCode}", ex.StatusCode);
            var errorDetail = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "GitHub API 请求频率超限，请稍后再试",
                System.Net.HttpStatusCode.NotFound => "未找到发布信息",
                _ => $"网络请求失败: {ex.Message}"
            };
            var result = UpdateCheckResult.Failure(currentVersion, errorDetail);
            CacheResult(result);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "检查更新失败");
            var result = UpdateCheckResult.Failure(currentVersion, ex.Message);
            CacheResult(result);
            return result;
        }
    }

    /// <summary>
    /// 下载安装包到临时目录，报告下载进度
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        GitHubRelease release,
        IProgress<double> progress,
        CancellationToken ct)
    {
        var asset = release.Assets
            .FirstOrDefault(a => a.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到安装包");

        var tempPath = Path.Combine(Path.GetTempPath(), asset.Name);

        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempPath, FileMode.Create);

        var buffer = new byte[81920];
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

    /// <summary>清除缓存，下次检查一定请求网络</summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedResult = null;
            _lastCheckTime = DateTime.MinValue;
        }
    }

    private void CacheResult(UpdateCheckResult result)
    {
        lock (_cacheLock)
        {
            _cachedResult = result;
            _lastCheckTime = DateTime.UtcNow;
        }
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
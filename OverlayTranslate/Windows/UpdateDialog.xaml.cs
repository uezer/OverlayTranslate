using System.Windows;
using OverlayTranslate.Services;

namespace OverlayTranslate.Windows;

public partial class UpdateDialog : Window
{
    private readonly UpdateService _updateService;
    private readonly GitHubRelease _release;
    private CancellationTokenSource? _cts;

    public UpdateDialog(UpdateService updateService, GitHubRelease release)
    {
        _updateService = updateService;
        _release = release;
        InitializeComponent();

        Title = "发现新版本";
        VersionRun.Text = $"v{release.TagName.TrimStart('v')}";
        DateText.Text = release.PublishedAt.ToString("yyyy-MM-dd");
        ChangelogText.Text = string.IsNullOrWhiteSpace(release.Body)
            ? "暂无更新说明"
            : release.Body;
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SkipButton.Content = "取消";
        DownloadProgress.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "正在下载...";

        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress.Value = p;
                StatusText.Text = $"下载中... {p:F0}%";
            });

            var installerPath = await _updateService.DownloadInstallerAsync(_release, progress, _cts.Token);

            StatusText.Text = "下载完成，即将安装...";
            await Task.Delay(1000);

            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "下载已取消";
            ResetButtons();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败: {ex.Message}";
            ResetButtons();
        }
    }

    private void OnSkipClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        DialogResult = false;
        Close();
    }

    private void ResetButtons()
    {
        UpdateButton.IsEnabled = true;
        SkipButton.Content = "稍后提醒";
        DownloadProgress.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnClosed(e);
    }
}

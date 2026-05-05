using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Localization;
using OverlayTranslate.Services;
using Serilog;

namespace OverlayTranslate.ViewModels;

public partial class UpdateDialogViewModel : ObservableObject
{
    private readonly UpdateService _updateService;
    private readonly ConfigManager _configManager;
    private readonly GitHubRelease _release;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _versionText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _changelogText = "";
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _canUpdate = true;

    public UpdateDialogViewModel(UpdateService updateService, ConfigManager configManager, GitHubRelease release)
    {
        _updateService = updateService;
        _configManager = configManager;
        _release = release;

        VersionText = $"v{release.TagName.TrimStart('v')}";
        DateText = release.PublishedAt.ToString("yyyy-MM-dd");
        ChangelogText = string.IsNullOrWhiteSpace(release.Body)
            ? LocManager.Get("Update_NoChangelog")
            : release.Body;
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        CanUpdate = false;
        IsDownloading = true;
        StatusText = LocManager.Get("Update_Downloading");
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StatusText = $"{LocManager.Get("Update_Downloading")} {p:F0}%";
            });

            var installerPath = await _updateService.DownloadInstallerAsync(
                _release, progress, _cts.Token);

            StatusText = LocManager.Get("Update_InstallReady");
            await Task.Delay(1000);

            _updateService.LaunchInstallerAndExit(installerPath);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocManager.Get("Update_Cancelled");
            ResetState();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "下载更新失败");
            StatusText = string.Format(LocManager.Get("Update_DownloadFailed"), ex.Message);
            ResetState();
        }
    }

    [RelayCommand]
    private void Cancel(Window? window)
    {
        _cts?.Cancel();
        if (window != null)
        {
            window.DialogResult = false;
            window.Close();
        }
    }

    [RelayCommand]
    private void SkipVersion()
    {
        _cts?.Cancel();
        _configManager.Settings.Update.SkippedVersion = _release.TagName.TrimStart('v');
        _configManager.Save();
    }

    private void ResetState()
    {
        CanUpdate = true;
        IsDownloading = false;
        DownloadProgress = 0;
    }

    public void Cleanup()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

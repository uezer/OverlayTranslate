using System.Drawing;
using System.Windows.Threading;
using OverlayTranslate.Models;
using Rectangle = System.Drawing.Rectangle;

namespace OverlayTranslate.Services;

public sealed class OverlaySessionController : IOverlaySessionController, IDisposable
{
    private static readonly TimeSpan OcrTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(6);

    private readonly ISettingsStore _settingsStore;
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IOcrEngine _ocrEngine;
    private readonly ITranslationProvider _translationProvider;
    private readonly IInPlaceRenderer _renderer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OverlayWindow? _overlayWindow;
    private Bitmap? _baseScreenshot;
    private Rectangle? _lastSelection;
    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _processingCancellation;

    public OverlaySessionState State { get; private set; } = OverlaySessionState.Idle;

    public OverlaySessionController(
        ISettingsStore settingsStore,
        IScreenCaptureService screenCaptureService,
        IOcrEngine ocrEngine,
        ITranslationProvider translationProvider,
        IInPlaceRenderer renderer)
    {
        _settingsStore = settingsStore;
        _screenCaptureService = screenCaptureService;
        _ocrEngine = ocrEngine;
        _translationProvider = translationProvider;
        _renderer = renderer;
    }

    public async Task StartCaptureAsync()
    {
        AppLogger.Info("StartCaptureAsync entered.");
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            await ReturnToTrayAsync().ConfigureAwait(true);

            _sessionCancellation = new CancellationTokenSource();
            _baseScreenshot = _screenCaptureService.CapturePrimaryScreen();
            AppLogger.Info($"Primary screen captured. Size={_baseScreenshot.Width}x{_baseScreenshot.Height}.");

            _overlayWindow = new OverlayWindow();
            _overlayWindow.SelectionCommitted += OnSelectionCommitted;
            _overlayWindow.ExitRequested += OnExitRequested;
            _overlayWindow.ReselectRequested += OnReselectRequested;
            _overlayWindow.RetryRequested += OnRetryRequested;
            _overlayWindow.ShowScreenshot(_baseScreenshot);
            _overlayWindow.Show();
            _overlayWindow.Activate();
            State = OverlaySessionState.Selecting;
            AppLogger.Info("Overlay window shown. State=Selecting.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        AppLogger.Info("OverlaySessionController dispose.");
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _baseScreenshot?.Dispose();
        _overlayWindow?.Close();

        if (_translationProvider is IDisposable disposableTranslationProvider)
        {
            disposableTranslationProvider.Dispose();
        }

        if (_ocrEngine is IDisposable disposableOcrEngine)
        {
            disposableOcrEngine.Dispose();
        }
    }

    private async void OnSelectionCommitted(object? sender, Rectangle selection)
    {
        AppLogger.Info($"Selection committed. X={selection.X}, Y={selection.Y}, W={selection.Width}, H={selection.Height}.");
        await ProcessSelectionAsync(selection, retried: false);
    }

    private async void OnRetryRequested(object? sender, EventArgs e)
    {
        if (_lastSelection is { } selection)
        {
            AppLogger.Info("Retry requested for last selection.");
            await ProcessSelectionAsync(selection, retried: true);
        }
    }

    private void OnReselectRequested(object? sender, EventArgs e)
    {
        AppLogger.Info("Reselect requested.");
        CancelProcessing();
        RestoreSelectionMode();
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        AppLogger.Info("Exit requested from overlay.");
        await ReturnToTrayAsync();
    }

    private async Task ProcessSelectionAsync(Rectangle selection, bool retried)
    {
        if (_overlayWindow is null || _baseScreenshot is null || _sessionCancellation is null)
        {
            return;
        }

        if (selection.Width < 20 || selection.Height < 20)
        {
            AppLogger.Warn($"Selection too small. W={selection.Width}, H={selection.Height}.");
            _overlayWindow.ShowError("区域过小，请重新选择。");
            RestoreSelectionMode();
            return;
        }

        _lastSelection = selection;
        CancelProcessing();
        _processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_sessionCancellation.Token);
        CancellationToken cancellationToken = _processingCancellation.Token;

        State = OverlaySessionState.Processing;
        _overlayWindow.ShowProcessing(selection, retried ? "正在重新翻译…" : "正在识别与翻译…");
        AppLogger.Info($"Processing selection started. Retried={retried}.");

        try
        {
            AppSettings settings = await _settingsStore.LoadAsync();
            AppLogger.Info($"Processing settings: Source={settings.SourceLanguage}, Target={settings.TargetLanguage}, TranslationStrategy={settings.TranslationStrategy}, OcrStrategy={settings.OcrStrategy}.");
            using Bitmap selectionBitmap = await Task.Run(
                () => _screenCaptureService.Crop(_baseScreenshot, selection),
                cancellationToken);
            AppLogger.Info($"Selection bitmap cropped. Size={selectionBitmap.Width}x{selectionBitmap.Height}.");

            await InvokeOnUiAsync(() => _overlayWindow?.ShowProcessing(selection, "正在识别文字…"));
            IReadOnlyList<OcrBlock> blocks = await RunWithTimeoutAsync(
                ct => Task.Run(async () => await _ocrEngine.RecognizeAsync(selectionBitmap, settings.SourceLanguage, ct), ct),
                OcrTimeout,
                cancellationToken);
            AppLogger.Info($"OCR finished. Blocks={blocks.Count}.");
            if (blocks.Count == 0)
            {
                await InvokeOnUiAsync(() => _overlayWindow?.ShowError("未识别到文字。"));
                State = OverlaySessionState.ResultShown;
                return;
            }

            List<TranslationSegment> segments = blocks
                .Select((block, index) =>
                {
                    string sourceText = block.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sourceText) && block.Lines.Count > 0)
                    {
                        sourceText = string.Join(
                            " ",
                            block.Lines
                                .Select(line => line.Text?.Trim())
                                .Where(text => !string.IsNullOrWhiteSpace(text)));
                    }

                    return new TranslationSegment(index, block.Bounds, sourceText);
                })
                .Where(segment => !string.IsNullOrWhiteSpace(segment.SourceText))
                .ToList();

            if (segments.Count == 0)
            {
                AppLogger.Warn("OCR returned blocks, but no non-empty translation segments.");
                await InvokeOnUiAsync(() => _overlayWindow?.ShowError("未识别到可翻译文字。"));
                State = OverlaySessionState.ResultShown;
                return;
            }

            string sourceLanguage = settings.SourceLanguage switch
            {
                SourceLanguage.Auto => "auto",
                SourceLanguage.Chinese => "zh",
                SourceLanguage.English => "en",
                SourceLanguage.Japanese => "ja",
                _ => "auto",
            };

            string targetLanguage = AppSettings.ResolveTargetLanguageCode(settings.TargetLanguage);
            await InvokeOnUiAsync(() => _overlayWindow?.ShowProcessing(selection, "正在翻译内容…"));
            AppLogger.Info($"Translation started. Segments={segments.Count}, Source={sourceLanguage}, Target={targetLanguage}.");
            IReadOnlyList<TranslationResult> translations = await _translationProvider
                .TranslateAsync(segments, sourceLanguage, targetLanguage, cancellationToken)
                .WaitAsync(TranslationTimeout, cancellationToken);
            AppLogger.Info($"Translation finished. Results={translations.Count}.");

            await InvokeOnUiAsync(() => _overlayWindow?.ShowProcessing(selection, "正在回绘结果…"));
            Bitmap rendered = await RunWithTimeoutAsync(
                ct => Task.Run(() => _renderer.Render(_baseScreenshot, selection, translations), ct),
                RenderTimeout,
                cancellationToken);
            AppLogger.Info("Render finished.");

            State = OverlaySessionState.ResultShown;
            await InvokeOnUiAsync(() =>
            {
                _baseScreenshot?.Dispose();
                _baseScreenshot = rendered;
                _overlayWindow?.ShowRenderedResult(_baseScreenshot, selection, "翻译完成");
            });
            AppLogger.Info("Result shown.");
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn("Processing canceled.");
        }
        catch (Exception exception)
        {
            State = OverlaySessionState.ResultShown;
            AppLogger.Error("Processing failed.", exception);
            await InvokeOnUiAsync(() => _overlayWindow?.ShowError($"处理失败：{exception.Message}"));
        }
    }

    private void RestoreSelectionMode()
    {
        if (_overlayWindow is null || _baseScreenshot is null)
        {
            return;
        }

        State = OverlaySessionState.Selecting;
        AppLogger.Info("Restore selection mode.");
        _overlayWindow.ShowRenderedResult(_baseScreenshot, null, string.Empty);
        _overlayWindow.EnableSelectionMode();
    }

    private async Task ReturnToTrayAsync()
    {
        AppLogger.Info($"ReturnToTrayAsync entered. CurrentState={State}.");
        State = OverlaySessionState.ReturningToTray;
        CancelProcessing();
        _sessionCancellation?.Cancel();
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;

        if (_overlayWindow is not null)
        {
            _overlayWindow.SelectionCommitted -= OnSelectionCommitted;
            _overlayWindow.ExitRequested -= OnExitRequested;
            _overlayWindow.ReselectRequested -= OnReselectRequested;
            _overlayWindow.RetryRequested -= OnRetryRequested;
            _overlayWindow.Close();
            _overlayWindow = null;
        }

        _baseScreenshot?.Dispose();
        _baseScreenshot = null;
        _lastSelection = null;
        State = OverlaySessionState.Idle;
        AppLogger.Info("Returned to tray. State=Idle.");
        await Task.CompletedTask;
    }

    private void CancelProcessing()
    {
        AppLogger.Info("CancelProcessing invoked.");
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
        _processingCancellation = null;
    }

    private Task InvokeOnUiAsync(Action action)
    {
        if (_overlayWindow?.Dispatcher is not Dispatcher dispatcher || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> work,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<T> workTask = work(timeoutCts.Token);
        Task delayTask = Task.Delay(timeout, cancellationToken);
        Task completedTask = await Task.WhenAny(workTask, delayTask);

        if (completedTask == delayTask)
        {
            timeoutCts.Cancel();
            AppLogger.Warn($"Stage timeout. TimeoutSeconds={timeout.TotalSeconds:0}.");
            throw new TimeoutException($"处理超时（{timeout.TotalSeconds:0} 秒）。");
        }

        timeoutCts.Cancel();
        return await workTask;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Media.Imaging;
using OverlayTranslate.Engines;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;
using OverlayTranslate.Services;
using Serilog;

namespace OverlayTranslate.ViewModels;

public partial class OverlayWindowViewModel : ObservableObject
{
    private readonly TranslationPipeline _pipeline;
    private readonly ConfigManager _configManager;
    private readonly Dictionary<string, IOcrEngine> _ocrEngines;
    private readonly Dictionary<string, ITranslationEngine> _translationEngines;

    // ===== 可观察属性 =====

    [ObservableProperty] private OverlayState _state = OverlayState.Idle;
    [ObservableProperty] private OverlayViewMode _viewMode = OverlayViewMode.OriginalText;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private BitmapImage? _backgroundSource;
    [ObservableProperty] private bool _toolbarVisible;
    [ObservableProperty] private string _originalText = "";
    [ObservableProperty] private string _translatedText = "";

    // ===== 缓存字段（不需要 UI 通知） =====

    public byte[]? ScreenshotData { get; set; }
    public Rect CurrentSelection { get; set; }
    public double ScreenshotDpiX { get; set; } = 96;
    public double ScreenshotDpiY { get; set; } = 96;
    public string CurrentOcrEngineName { get; set; } = "";
    public string CurrentTranslationEngineName { get; set; } = "";

    // 管线结果缓存
    public IReadOnlyList<TextBlock>? LastOcrTextBlocks { get; set; }
    public IReadOnlyList<(string Text, Rect BoundingBox)>? TranslatedBlocks { get; set; }
    public byte[]? FilledImageBytes { get; set; }
    public TextStyleInfo? OriginalStyle { get; set; }
    public TextStyleInfo? TranslatedStyle { get; set; }

    public CancellationTokenSource Cts { get; private set; } = new();

    public OverlayWindowViewModel(
        TranslationPipeline pipeline,
        ConfigManager configManager,
        Dictionary<string, IOcrEngine> ocrEngines,
        Dictionary<string, ITranslationEngine> translationEngines)
    {
        _pipeline = pipeline;
        _configManager = configManager;
        _ocrEngines = ocrEngines;
        _translationEngines = translationEngines;

        CurrentOcrEngineName = configManager.Settings.Ocr.ActiveEngine;
        CurrentTranslationEngineName = configManager.Settings.Translation.ActiveEngine;

        // 确保引擎名有效
        if (!ocrEngines.ContainsKey(CurrentOcrEngineName))
            CurrentOcrEngineName = ocrEngines.Keys.FirstOrDefault() ?? "";
        if (!translationEngines.ContainsKey(CurrentTranslationEngineName))
            CurrentTranslationEngineName = translationEngines.Keys.FirstOrDefault() ?? "";

        Log.Information("配置加载: OCR={Ocr}, Translation={Trans}",
            CurrentOcrEngineName, CurrentTranslationEngineName);
    }

    // ===== 状态管理 =====

    public void CancelAndStart()
    {
        Cts.Cancel();
        Cts.Dispose();
        Cts = new CancellationTokenSource();
    }

    public void ClearForReselect()
    {
        CancelAndStart();
        State = OverlayState.Selecting;
        ToolbarVisible = false;
    }

    public void ClearForExit()
    {
        State = OverlayState.Exiting;
        Cts.Cancel();
        ScreenshotData = null;
        LastOcrTextBlocks = null;
        TranslatedBlocks = null;
        FilledImageBytes = null;
        OriginalStyle = null;
        TranslatedStyle = null;
        BackgroundSource = null;
        ToolbarVisible = false;
        State = OverlayState.Idle;
    }

    // ===== 引擎/语言管理 =====

    public string SourceLanguage
    {
        get => _configManager.Settings.Language.Source;
        set
        {
            _configManager.Settings.Language.Source = value;
            _configManager.Save();
        }
    }

    public string TargetLanguage
    {
        get => _configManager.Settings.Language.Target;
        set
        {
            _configManager.Settings.Language.Target = value;
            _configManager.Save();
        }
    }

    public void SwitchOcrEngine(string engineName)
    {
        if (!_ocrEngines.ContainsKey(engineName)) return;
        CurrentOcrEngineName = engineName;
        _configManager.Settings.Ocr.ActiveEngine = engineName;
        _configManager.Save();
        Log.Information("切换 OCR 引擎: {Engine}", engineName);
    }

    public void SwitchTranslationEngine(string engineName)
    {
        if (!_translationEngines.ContainsKey(engineName)) return;
        CurrentTranslationEngineName = engineName;
        _configManager.Settings.Translation.ActiveEngine = engineName;
        _configManager.Save();
        Log.Information("切换翻译引擎: {Engine}", engineName);
    }
}

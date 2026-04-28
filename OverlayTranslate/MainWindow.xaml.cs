using System.Windows;
using OverlayTranslate.Models;
using OverlayTranslate.Services;

namespace OverlayTranslate;

public partial class MainWindow : Window
{
    private readonly ISettingsStore _settingsStore;
    private AppSettings _settings = new();

    public MainWindow(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        InitializeComponent();
        PopulateOptions();
    }

    public void LoadSettings(AppSettings settings)
    {
        _settings = settings.Clone();
        SourceLanguageComboBox.SelectedValue = _settings.SourceLanguage;
        TargetLanguageComboBox.SelectedValue = _settings.TargetLanguage;
        OcrStrategyComboBox.SelectedValue = _settings.OcrStrategy;
        TranslationStrategyComboBox.SelectedValue = _settings.TranslationStrategy;
        OnlineEndpointTextBox.Text = _settings.OnlineTranslationEndpoint;
        StartOnLaunchCheckBox.IsChecked = _settings.StartCaptureOnLaunch;
        StatusTextBlock.Text = "当前版本实际启用本地 PaddleOCRSharp。在线 OCR 策略项已预留，后续可接入远端 Provider。";
    }

    private void PopulateOptions()
    {
        SourceLanguageComboBox.ItemsSource = new[]
        {
            new EnumOption<SourceLanguage>("自动检测", SourceLanguage.Auto),
            new EnumOption<SourceLanguage>("中文", SourceLanguage.Chinese),
            new EnumOption<SourceLanguage>("英文", SourceLanguage.English),
            new EnumOption<SourceLanguage>("日文", SourceLanguage.Japanese),
        };
        SourceLanguageComboBox.DisplayMemberPath = nameof(EnumOption<SourceLanguage>.Label);
        SourceLanguageComboBox.SelectedValuePath = nameof(EnumOption<SourceLanguage>.Value);

        TargetLanguageComboBox.ItemsSource = new[]
        {
            new EnumOption<TargetLanguage>("中文", TargetLanguage.Chinese),
            new EnumOption<TargetLanguage>("英文", TargetLanguage.English),
            new EnumOption<TargetLanguage>("日文", TargetLanguage.Japanese),
            new EnumOption<TargetLanguage>("系统语言", TargetLanguage.System),
        };
        TargetLanguageComboBox.DisplayMemberPath = nameof(EnumOption<TargetLanguage>.Label);
        TargetLanguageComboBox.SelectedValuePath = nameof(EnumOption<TargetLanguage>.Value);

        OcrStrategyComboBox.ItemsSource = new[]
        {
            new EnumOption<OcrStrategy>("仅本地", OcrStrategy.LocalOnly),
            new EnumOption<OcrStrategy>("本地优先，失败时在线", OcrStrategy.LocalFirstThenOnline),
            new EnumOption<OcrStrategy>("仅在线", OcrStrategy.OnlineOnly),
        };
        OcrStrategyComboBox.DisplayMemberPath = nameof(EnumOption<OcrStrategy>.Label);
        OcrStrategyComboBox.SelectedValuePath = nameof(EnumOption<OcrStrategy>.Value);

        TranslationStrategyComboBox.ItemsSource = new[]
        {
            new EnumOption<TranslationStrategy>("仅本地", TranslationStrategy.LocalOnly),
            new EnumOption<TranslationStrategy>("本地优先，失败时在线", TranslationStrategy.LocalFirstThenOnline),
            new EnumOption<TranslationStrategy>("仅在线", TranslationStrategy.OnlineOnly),
        };
        TranslationStrategyComboBox.DisplayMemberPath = nameof(EnumOption<TranslationStrategy>.Label);
        TranslationStrategyComboBox.SelectedValuePath = nameof(EnumOption<TranslationStrategy>.Value);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.SourceLanguage = (SourceLanguage)(SourceLanguageComboBox.SelectedValue ?? SourceLanguage.Auto);
        _settings.TargetLanguage = (TargetLanguage)(TargetLanguageComboBox.SelectedValue ?? TargetLanguage.System);
        _settings.OcrStrategy = (OcrStrategy)(OcrStrategyComboBox.SelectedValue ?? OcrStrategy.LocalOnly);
        _settings.TranslationStrategy = (TranslationStrategy)(TranslationStrategyComboBox.SelectedValue ?? TranslationStrategy.LocalFirstThenOnline);
        _settings.OnlineTranslationEndpoint = OnlineEndpointTextBox.Text.Trim();
        _settings.StartCaptureOnLaunch = StartOnLaunchCheckBox.IsChecked == true;

        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        StatusTextBlock.Text = "设置已保存，将在下一次截图流程中生效。";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private sealed record EnumOption<T>(string Label, T Value);
}

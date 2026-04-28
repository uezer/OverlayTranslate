using System.Globalization;

namespace OverlayTranslate.Models;

public sealed class AppSettings
{
    public SourceLanguage SourceLanguage { get; set; } = SourceLanguage.Auto;

    public TargetLanguage TargetLanguage { get; set; } = TargetLanguage.System;

    public OcrStrategy OcrStrategy { get; set; } = OcrStrategy.LocalOnly;

    public TranslationStrategy TranslationStrategy { get; set; } = TranslationStrategy.LocalFirstThenOnline;

    public string OnlineTranslationEndpoint { get; set; } = string.Empty;

    public bool StartCaptureOnLaunch { get; set; } = true;

    public static string ResolveTargetLanguageCode(TargetLanguage targetLanguage)
    {
        return targetLanguage switch
        {
            TargetLanguage.Chinese => "zh",
            TargetLanguage.English => "en",
            TargetLanguage.Japanese => "ja",
            TargetLanguage.System => ResolveSystemLanguageCode(),
            _ => "en",
        };
    }

    public AppSettings Clone()
    {
        return new AppSettings
        {
            SourceLanguage = SourceLanguage,
            TargetLanguage = TargetLanguage,
            OcrStrategy = OcrStrategy,
            TranslationStrategy = TranslationStrategy,
            OnlineTranslationEndpoint = OnlineTranslationEndpoint,
            StartCaptureOnLaunch = StartCaptureOnLaunch,
        };
    }

    private static string ResolveSystemLanguageCode()
    {
        string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return language switch
        {
            "zh" => "zh",
            "ja" => "ja",
            _ => "en",
        };
    }
}

public enum SourceLanguage
{
    Auto,
    Chinese,
    English,
    Japanese,
}

public enum TargetLanguage
{
    Chinese,
    English,
    Japanese,
    System,
}

public enum TranslationStrategy
{
    LocalOnly,
    LocalFirstThenOnline,
    OnlineOnly,
}

public enum OcrStrategy
{
    LocalOnly,
    LocalFirstThenOnline,
    OnlineOnly,
}

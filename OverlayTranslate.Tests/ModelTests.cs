using System.Windows;
using OverlayTranslate.Infrastructure;
using OverlayTranslate.Models;

namespace OverlayTranslate.Tests;

public class ModelTests
{
    [Fact]
    public void OcrResult_DefaultValues()
    {
        var result = new OcrResult();

        Assert.NotNull(result.TextBlocks);
        Assert.Empty(result.TextBlocks);
        Assert.Equal("", result.FullText);
        Assert.Equal("", result.Language);
    }

    [Fact]
    public void OcrResult_FullText_CanBeSet()
    {
        var result = new OcrResult { FullText = "Hello World" };
        Assert.Equal("Hello World", result.FullText);
    }

    [Fact]
    public void TextBlock_DefaultValues()
    {
        var block = new TextBlock();

        Assert.Equal("", block.Text);
        Assert.Equal(default, block.BoundingBox);
        Assert.Equal(0f, block.Confidence);
        Assert.Equal(0f, block.Angle);
    }

    [Fact]
    public void TextBlock_BoundingBox_CanBeSet()
    {
        var rect = new Rect(10, 20, 100, 50);
        var block = new TextBlock { BoundingBox = rect };

        Assert.Equal(10, block.BoundingBox.X);
        Assert.Equal(20, block.BoundingBox.Y);
        Assert.Equal(100, block.BoundingBox.Width);
        Assert.Equal(50, block.BoundingBox.Height);
    }

    [Fact]
    public void TextBlock_Confidence_CanBeSet()
    {
        var block = new TextBlock { Confidence = 0.95f };
        Assert.InRange(block.Confidence, 0.94f, 0.96f);
    }

    [Fact]
    public void TranslationResult_DefaultValues()
    {
        var result = new TranslationResult();

        Assert.Equal("", result.TranslatedText);
        Assert.Equal("", result.SourceLanguage);
        Assert.Equal("", result.EngineName);
    }

    [Fact]
    public void TranslationResult_CanBeSet()
    {
        var result = new TranslationResult
        {
            TranslatedText = "你好",
            SourceLanguage = "en",
            EngineName = "DeepL"
        };

        Assert.Equal("你好", result.TranslatedText);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("DeepL", result.EngineName);
    }

    [Fact]
    public void AppSettings_SubSettings_AreInitialized()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.Ocr);
        Assert.NotNull(settings.Translation);
        Assert.NotNull(settings.Hotkey);
        Assert.NotNull(settings.Language);
        Assert.NotNull(settings.Python);
        Assert.NotNull(settings.Logging);
        Assert.NotNull(settings.Other);
    }

    [Fact]
    public void AppSettings_HasOtherSettings_WithDefaults()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.Other);
        Assert.Equal("auto", settings.Other.FontSizeMode);
        Assert.Equal(14, settings.Other.CustomFontSize);
        Assert.Equal("system", settings.Other.Theme);
    }

    [Fact]
    public void OcrResult_TextBlocks_CanAddMultiple()
    {
        var result = new OcrResult();
        result.TextBlocks.Add(new TextBlock { Text = "Hello" });
        result.TextBlocks.Add(new TextBlock { Text = "World" });

        Assert.Equal(2, result.TextBlocks.Count);
        Assert.Equal("Hello", result.TextBlocks[0].Text);
        Assert.Equal("World", result.TextBlocks[1].Text);
    }

    [Fact]
    public void TextStyleInfo_DefaultValues()
    {
        var style = new Services.TextStyleInfo();

        Assert.Equal(0, style.FontSize);
        Assert.Equal(default, style.TextColor);
        Assert.False(style.IsBold);
        Assert.Equal(0, style.RegionWidth);
        Assert.Equal(0, style.RegionHeight);
    }

    [Fact]
    public void TextStyleInfo_CanBeSet()
    {
        var style = new Services.TextStyleInfo
        {
            FontSize = 16,
            TextColor = System.Windows.Media.Colors.Red,
            IsBold = true,
            RegionWidth = 200,
            RegionHeight = 50
        };

        Assert.Equal(16, style.FontSize);
        Assert.Equal(System.Windows.Media.Colors.Red, style.TextColor);
        Assert.True(style.IsBold);
        Assert.Equal(200, style.RegionWidth);
        Assert.Equal(50, style.RegionHeight);
    }

    [Fact]
    public void ThemeManager_GetSystemTheme_ReturnsValidValue()
    {
        var theme = ThemeManager.GetSystemTheme();
        Assert.Contains(theme, new[] { "light", "dark" });
    }

    [Fact]
    public void ThemeManager_SetTheme_DoesNotThrow()
    {
        // 需要 Application 上下文，此测试仅验证不抛异常
        // 在非 WPF 环境中跳过
        if (Application.Current == null) return;
        var ex = Record.Exception(() =>
            ThemeManager.SetTheme("dark"));
        Assert.Null(ex);
    }
}

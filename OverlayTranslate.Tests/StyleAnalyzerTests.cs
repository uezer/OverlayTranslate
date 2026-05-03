using System.Windows;
using System.Windows.Media;
using OverlayTranslate.Services;

namespace OverlayTranslate.Tests;

public class StyleAnalyzerTests
{
    private readonly StyleAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_ReturnsFontSizeBasedOnRegionHeight()
    {
        var region = new Rect(0, 0, 200, 40);

        var result = _analyzer.Analyze(region, "Hello World");

        Assert.Equal(30, result.FontSize); // 40 * 0.75 = 30
    }

    [Fact]
    public void Analyze_ClampsFontSize_Minimum8()
    {
        var region = new Rect(0, 0, 200, 5);

        var result = _analyzer.Analyze(region, "Hi");

        Assert.Equal(8, result.FontSize); // 5 * 0.75 = 3.75, clamped to 8
    }

    [Fact]
    public void Analyze_ClampsFontSize_Maximum72()
    {
        var region = new Rect(0, 0, 200, 200);

        var result = _analyzer.Analyze(region, "Big text");

        Assert.Equal(72, result.FontSize); // 200 * 0.75 = 150, clamped to 72
    }

    [Fact]
    public void Analyze_SetsTextColorToBlack()
    {
        var region = new Rect(0, 0, 100, 30);

        var result = _analyzer.Analyze(region, "test");

        Assert.Equal(Colors.Black, result.TextColor);
    }

    [Fact]
    public void Analyze_SetsIsBoldToFalse()
    {
        var region = new Rect(0, 0, 100, 30);

        var result = _analyzer.Analyze(region, "test");

        Assert.False(result.IsBold);
    }

    [Fact]
    public void Analyze_PreservesRegionDimensions()
    {
        var region = new Rect(10, 20, 300, 50);

        var result = _analyzer.Analyze(region, "test");

        Assert.Equal(300, result.RegionWidth);
        Assert.Equal(50, result.RegionHeight);
    }

    [Fact]
    public void AdjustFontSize_ReturnsOriginalSize_WhenTextFits()
    {
        var style = new TextStyleInfo
        {
            FontSize = 16,
            RegionWidth = 500
        };

        var result = _analyzer.AdjustFontSize("Hi", style);

        Assert.Equal(16, result);
    }

    [Fact]
    public void AdjustFontSize_ScalesDown_WhenTextTooLong()
    {
        var style = new TextStyleInfo
        {
            FontSize = 32,
            RegionWidth = 100
        };

        var result = _analyzer.AdjustFontSize("This is a very long translated text that should not fit", style);

        Assert.True(result < 32);
        Assert.True(result >= 8);
    }

    [Fact]
    public void AdjustFontSize_ClampsToMinimum8()
    {
        var style = new TextStyleInfo
        {
            FontSize = 10,
            RegionWidth = 1
        };

        var result = _analyzer.AdjustFontSize("Extremely long text that will definitely not fit in one pixel width", style);

        Assert.Equal(8, result);
    }

    [Fact]
    public void AdjustFontSize_ReturnsOriginalSize_ForEmptyText()
    {
        var style = new TextStyleInfo
        {
            FontSize = 20,
            RegionWidth = 200
        };

        var result = _analyzer.AdjustFontSize("", style);

        Assert.Equal(20, result);
    }

    [Fact]
    public void Analyze_WithSmallRegion_HandlesCorrectly()
    {
        var region = new Rect(0, 0, 10, 10);

        var result = _analyzer.Analyze(region, "x");

        Assert.Equal(8, result.FontSize); // 10 * 0.75 = 7.5, clamped to 8
        Assert.Equal(10, result.RegionWidth);
        Assert.Equal(10, result.RegionHeight);
    }
}

namespace OverlayTranslate.Models;

public class OcrResult
{
    public List<TextBlock> TextBlocks { get; set; } = [];
    public string FullText { get; set; } = "";
    public string Language { get; set; } = "";
}

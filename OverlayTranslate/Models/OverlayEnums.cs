namespace OverlayTranslate.Models;

public enum OverlayState
{
    Idle,
    Selecting,
    Processing,
    Result,
    Exiting
}

public enum OverlayViewMode
{
    OriginalImage,
    OriginalText,
    TranslatedText
}

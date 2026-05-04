using System.Linq;

namespace OverlayTranslate.Localization;

public class EngineDisplayMap
{
    private readonly Dictionary<string, string> _engineToLocKey = new()
    {
        ["PaddleOCR"] = "Engine_PaddleOCR",
        ["RemoteOCR"] = "Engine_RemoteOcr",
        ["DeepL"] = "Engine_DeepL",
        ["Google"] = "Engine_Google",
        ["百度"] = "Engine_Baidu",
        ["OpenAI"] = "Engine_OpenAI",
        ["Microsoft"] = "Engine_Microsoft"
    };

    public string GetDisplayName(string engineKey)
    {
        if (_engineToLocKey.TryGetValue(engineKey, out var locKey))
            return LocManager.Get(locKey);
        return engineKey;
    }

    public string GetEngineKey(string displayName)
    {
        foreach (var (key, locKey) in _engineToLocKey)
        {
            if (LocManager.Get(locKey) == displayName)
                return key;
        }
        return displayName;
    }

    public string[] GetLocalizedNames(string[] engineKeys)
    {
        return engineKeys.Select(GetDisplayName).ToArray();
    }
}

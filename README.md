# OverlayTranslate

[中文](README.zh-CN.md) | English

A Windows overlay translation tool that OCRs and translates text right on your screen — no switching windows, no copy-pasting.

## What Makes It Different

Most translation tools make you copy text, paste it somewhere, read the result, then switch back. OverlayTranslate skips all of that:

1. Press a hotkey (default: `Ctrl+Shift+T`) to capture a screen region
2. The app OCRs the text and translates it
3. The translation **replaces the original text directly on screen**

The translated text overlays the original content in place — you keep reading without breaking your flow.

## Features

- **Overlay Translation** — translated text appears on top of the original, with background color matching and auto-sized fonts
- **Select & Copy** — original and translated text can be selected and copied directly from the overlay
- **5 Translation Engines** — Google (free), Microsoft/Bing (free), DeepL, Baidu, OpenAI
- **2 OCR Engines** — PaddleOCR (offline, local) and Remote OCR (HTTP endpoint)
- **Engine Fallback** — automatic fallback when the selected engine is unavailable
- **Runtime Switching** — switch engines and languages on-the-fly via the floating toolbar
- **3 View Modes** — Original Image / Original Text / Translated Text
- **Auto Text Color** — white on dark backgrounds, black on light backgrounds
- **Light / Dark / System Theme** — full theme support

## Install

Download the latest installer from [Releases](https://github.com/Ezer013/OverlayTranslate/releases).

**Requirements:** Windows 10/11 (x64) + [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Quick Start

### Requirements

- Windows 10/11 (x64)
- .NET 10.0 Runtime

### Build from Source

```bash
git clone https://github.com/Ezer013/OverlayTranslate.git
cd OverlayTranslate
dotnet build
dotnet run --project OverlayTranslate
```

Build installer (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
.\build.ps1
```

### Usage

1. The app runs in the system tray
2. Press `Ctrl+Shift+T` or click the tray icon to start a capture
3. Drag to select the region containing text
4. The app OCRs and translates — the overlay appears automatically
5. Use the floating toolbar to switch views, engines, or languages
6. Press `Esc` or right-click to exit the overlay

## Translation Engines

| Engine | API Key Required | Notes |
|--------|-----------------|-------|
| Google Translate | No | Free, always available |
| Microsoft/Bing | No | Free, auto-detects regional domain |
| DeepL | Yes | Supports free and pro tiers |
| Baidu Translate | Yes | Requires App ID + Secret |
| OpenAI | Yes | Configurable model (default: gpt-4o-mini) |

## Configuration

Settings are stored in `OverlayTranslate/Config/appsettings.json` and can also be edited via the Settings window (tray icon → Settings).

Key settings:

- **OCR engine** and fallback engine
- **Translation engine** and fallback engine
- **Source / target language** (auto-detect supported)
- **Global hotkey**
- **Theme** (light / dark / system)
- **Font size mode** (auto / fit-width / custom)

## Project Structure

```
OverlayTranslate/
├── Controls/          # Floating toolbar, mask, selection canvas
├── Engines/
│   ├── Ocr/           # PaddleOCR, Remote OCR
│   └── Translation/   # Google, DeepL, Baidu, OpenAI, Microsoft
├── Infrastructure/    # Config, hotkeys, themes, tray icon
├── Services/          # Screenshot, image processing, style analysis
├── Themes/            # Light and Dark theme resources
└── Windows/           # Overlay window, Settings window
```

## License

This project is provided as-is for personal and educational use.

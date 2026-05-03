# OverlayTranslate 技术设计规格

## 1. 概述

OverlayTranslate 是一款 Windows 桌面截图翻译工具，通过全屏覆盖 + 框选区域的方式实现"即指即译"。本文档补充需求文档中的技术选型细节和架构设计。

## 2. 技术选型

| 层面 | 选型 | 说明 |
|------|------|------|
| 运行时 | .NET 10.0 (Windows) | 目标框架 |
| UI 框架 | WPF | 原生 Windows 支持，透明窗口能力好 |
| OCR | PaddleOCR (本地) + 远程 OCR API | 参考 Umi-OCR 的插件化引擎方式 |
| 翻译 | 插件化多引擎 | 参考 pot-app，支持 Google/DeepL/百度/有道/OpenAI 等 |
| 图像处理 | OpenCV (OpenCvSharp4) | 文字区域清理、背景色采样 |
| Python 互操作 | pythonnet | C# 缺失的库通过 Python.NET 调用 |
| 系统托盘 | H.NotifyIcon.Wpf | 托盘图标管理 |
| 日志 | Serilog + File Sink | 结构化日志 |
| DI | Microsoft.Extensions.DependencyInjection | .NET 内置 DI |
| 配置 | JSON 文件 | appsettings.json |
| 全局热键 | RegisterHotKey (Win32 API) | 自定义全局快捷键触发截图 |

## 3. 架构设计

### 3.1 分层架构

```
入口层        系统托盘 (H.NotifyIcon) + 全局快捷键 (RegisterHotKey)
    ↓
UI 层         OverlayWindow (覆盖层) + SelectionCanvas (选区) + FloatingToolbar (工具栏)
    ↓
核心处理层    ScreenshotService → ImageProcessor → TextRenderer → StyleAnalyzer
    ↓
引擎层        IOcrEngine / ITranslationEngine (插件化接口)
    ↓
基础设施层    DI 容器 + JSON 配置 + Serilog 日志 + OpenCV + Python.NET
```

### 3.2 插件化引擎架构

#### IOcrEngine 接口

```csharp
public interface IOcrEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<OcrResult> RecognizeAsync(byte[] imageData, OcrOptions options);
    string[] GetSupportedLanguages();
}
```

#### ITranslationEngine 接口

```csharp
public interface ITranslationEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<TranslationResult> TranslateAsync(string text, string from, string to);
    string[] GetSupportedLanguages();
}
```

#### 数据模型

```csharp
public class OcrResult
{
    public List<TextBlock> TextBlocks { get; set; }
    public string FullText { get; set; }
    public string Language { get; set; }
}

public class TextBlock
{
    public string Text { get; set; }
    public Rect BoundingBox { get; set; }
    public float Confidence { get; set; }
    public float Angle { get; set; }
}

public class TranslationResult
{
    public string TranslatedText { get; set; }
    public string SourceLanguage { get; set; }
    public string EngineName { get; set; }
}
```

#### 引擎选择策略

- **仅本地**：只使用本地引擎
- **本地优先**：本地失败时回退远程
- **仅远程**：只使用远程 API
- **自定义顺序**：用户拖拽排序

引擎注册通过 DI 容器，启动时扫描 `Engines/` 目录下的实现并注册。

### 3.3 Python.NET 互操作

#### 策略

- C# 生态成熟的库 → 直接用 C#（如 PaddleOCRSharp、OpenCvSharp）
- C# 缺失或实现困难的 → 通过 Python.NET 调用 Python 库

#### 组件

- **PythonRuntime.cs**：管理 Python 引擎生命周期，跟随 App 启停
- **PythonBridge.cs**：封装 C# ↔ Python 调用桥接
- **scripts/**：Python 脚本目录，按需放置

#### 配置

Python 运行时路径在 `appsettings.json` 中配置。

## 4. 覆盖层 UI 设计

### 4.1 窗口层次（Z-Order）

```
最底层  背景截图层（ScreenshotService 采集的屏幕截图）
  ↑    灰色遮罩层（MaskLayer，全屏半透明黑色）
  ↑    选区层（SelectionCanvas，无遮罩区域显示原截图）
  ↑    翻译结果层（TextRenderer，译文覆盖在原文位置）
最顶层  浮动工具栏（FloatingToolbar）
```

### 4.2 状态机

| 状态 | 说明 | 触发条件 |
|------|------|----------|
| S1 空闲驻留 | 最小化到托盘 | 初始状态 |
| S2 截图遮罩 | 显示覆盖层和遮罩，等待框选 | 托盘左键 / 全局快捷键 |
| S3 选区处理中 | 截图 → OCR → 翻译 → 覆盖 | 左键拖拽完成选区 |
| S4 结果展示 | 显示翻译结果，工具栏可操作 | 处理完成 |
| S5 退出回托盘 | 关闭覆盖层 | 任意状态按 Esc / 右键 |

S4 中点击"重选"回到 S2。

### 4.3 浮动工具栏

工具栏位于选区下方，包含三行控件：

**第一行 — 语言选择：**
- 原语言（默认"自动检测"，供 OCR 和翻译使用）
- 目标语言（翻译输出语言）

**第二行 — 引擎切换：**
- OCR 引擎下拉选择
- 翻译引擎下拉选择
- 切换后自动重新执行 OCR + 翻译

**第三行 — 操作按钮：**
- 重选：清除选区，回到 S2
- 显示原文：切换显示原文/译文（切换按钮）
- 复制原文：将 OCR 识别的原文复制到剪贴板
- 复制译文：将翻译结果复制到剪贴板
- 退出：关闭覆盖层，回到托盘

语言选择和引擎选择持久化到配置文件。

## 5. 翻译处理流程

```
选区截图 → OCR 识别 → 文字区域定位 → 背景色采样 → 原文覆盖
                                                      ↓
译文回绘 ← 字号适配 ← 样式分析 ← 翻译文本 ← 原文覆盖
```

### 5.1 原文字区域清理

参考 Umi-OCR 的原位覆盖方法：
1. OCR 返回每个文字块的边界框 (BoundingBox)
2. 对每个文字块区域采样周围背景色
3. 用采样颜色填充文字块区域（矩形覆盖）
4. 可选：使用 OpenCV inpaint 进行更精细的修复

### 5.2 译文回绘

1. 分析原文区域的字体大小、颜色、排版方向
2. 根据译文长度调整字号，确保不超出边界框
3. 在原文位置绘制译文

## 6. 配置结构 (appsettings.json)

```json
{
  "ocr": {
    "activeEngine": "PaddleOCR",
    "fallbackEngine": "RemoteOCR",
    "strategy": "LocalFirst",
    "engines": {
      "PaddleOCR": { "modelPath": "inference/" },
      "RemoteOCR": { "endpoint": "http://localhost:1224/api/ocr", "apiKey": "" }
    }
  },
  "translation": {
    "activeEngine": "DeepL",
    "fallbackEngine": "Google",
    "strategy": "LocalFirst",
    "engines": {
      "DeepL": { "apiKey": "", "freeTier": true },
      "Google": { "endpoint": "free" },
      "Baidu": { "appId": "", "secret": "" },
      "OpenAI": { "apiKey": "", "model": "gpt-4o-mini" }
    }
  },
  "hotkey": { "modifiers": ["Ctrl", "Shift"], "key": "T" },
  "language": { "source": "auto", "target": "zh-CN" },
  "python": { "runtimePath": "" },
  "logging": { "level": "Information", "file": "logs/app.log" }
}
```

## 7. NuGet 依赖

### 已集成
- PaddleOCRSharp（本地 OCR）
- OpenCvSharp4（图像处理）
- H.NotifyIcon.Wpf（系统托盘）

### 需要添加
- pythonnet（Python.NET 互操作）
- Serilog + Serilog.Sinks.File（日志）
- Microsoft.Extensions.DependencyInjection（DI）
- Microsoft.Extensions.Http（远程 API 调用）

## 8. 项目结构

```
OverlayTranslate/
├─ App.xaml / App.xaml.cs
├─ MainWindow.xaml (托盘窗口)
├─ Windows/
│   ├─ OverlayWindow.xaml (覆盖层)
│   └─ SettingsWindow.xaml (设置)
├─ Controls/
│   ├─ SelectionCanvas.cs (选区绘制)
│   ├─ MaskLayer.cs (遮罩层)
│   └─ FloatingToolbar.xaml (浮动工具栏)
├─ Services/
│   ├─ ScreenshotService.cs
│   ├─ ImageProcessor.cs
│   ├─ TextRenderer.cs
│   └─ StyleAnalyzer.cs
├─ Engines/
│   ├─ IOcrEngine.cs
│   ├─ ITranslationEngine.cs
│   ├─ Ocr/
│   │   ├─ PaddleOcrEngine.cs
│   │   ├─ RemoteOcrEngine.cs
│   │   └─ WindowsOcrEngine.cs
│   └─ Translation/
│       ├─ DeepLTranslationEngine.cs
│       ├─ GoogleTranslationEngine.cs
│       ├─ BaiduTranslationEngine.cs
│       └─ OpenAiTranslationEngine.cs
├─ Models/
│   ├─ OcrResult.cs, TextBlock.cs
│   ├─ TranslationResult.cs
│   └─ AppSettings.cs
├─ Infrastructure/
│   ├─ HotkeyManager.cs
│   ├─ ConfigManager.cs
│   └─ TrayIconManager.cs
├─ Python/
│   ├─ PythonRuntime.cs
│   ├─ PythonBridge.cs
│   └─ scripts/
└─ Config/
    └─ appsettings.json
```

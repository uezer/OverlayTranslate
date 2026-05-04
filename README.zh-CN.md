# OverlayTranslate

English | [中文](README.zh-CN.md)

Windows 覆盖翻译工具——框选屏幕区域，OCR 识别原文，译文直接覆盖显示在原位，无需切换窗口、无需复制粘贴。

## 覆盖翻译，所见即所得

传统翻译工具的流程：截图 → 复制文字 → 粘贴到翻译窗口 → 看结果 → 切回原来的地方。

OverlayTranslate 把这个流程压缩成一步：

1. 按快捷键（默认 `Ctrl+Shift+T`），框选屏幕上有文字的区域
2. 自动 OCR 识别 + 翻译
3. 译文直接覆盖在原文上方，背景色自动匹配

读到哪翻到哪，视线不用离开屏幕。

## 功能特性

- **覆盖翻译**——译文直接叠加在原文位置，背景色自动填充，字号自适应
- **文字可选中复制**——原文和译文都可以用鼠标选中，Ctrl+C 复制
- **5 个翻译引擎**——Google（免费）、Microsoft/Bing（免费）、DeepL、百度、OpenAI
- **2 个 OCR 引擎**——PaddleOCR（离线本地）和远程 OCR（HTTP 端点）
- **引擎自动回退**——主引擎不可用时自动切换到备用引擎
- **运行时切换**——工具栏上随时切换引擎和语言
- **三种视图模式**——原图 / 原文覆盖 / 译文覆盖
- **智能文字颜色**——深色背景用白字，浅色背景用黑字
- **浅色 / 深色 / 跟随系统主题**

## 安装

从 [Releases](https://github.com/Ezer013/OverlayTranslate/releases) 下载最新版安装包。

**环境要求：** Windows 10/11（x64）+ [.NET 10.0 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)

### 从源码构建

```bash
git clone https://github.com/Ezer013/OverlayTranslate.git
cd OverlayTranslate
dotnet build
dotnet run --project OverlayTranslate
```

打包安装包（需安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)）：

```powershell
.\build.ps1
```

## 快速开始

### 使用方法

1. 应用启动后常驻系统托盘
2. 按 `Ctrl+Shift+T` 或点击托盘图标，开始截取屏幕区域
3. 拖拽鼠标框选包含文字的区域
4. 自动完成 OCR 和翻译，覆盖层随即出现
5. 通过浮动工具栏切换视图、引擎或语言
6. 按 `Esc` 或右键点击退出覆盖层

## 翻译引擎一览

| 引擎 | 需要 API Key | 说明 |
|------|------------|------|
| Google Translate | 否 | 免费，始终可用 |
| Microsoft/Bing | 否 | 免费，自动检测区域域名 |
| DeepL | 是 | 支持免费版和专业版 |
| 百度翻译 | 是 | 需要 App ID 和密钥 |
| OpenAI | 是 | 模型可配置，默认 gpt-4o-mini |

## 配置

设置保存在 `OverlayTranslate/Config/appsettings.json`，也可通过托盘菜单中的「设置」窗口修改。

主要配置项：

- **OCR 引擎**及回退引擎
- **翻译引擎**及回退引擎
- **源语言 / 目标语言**（支持自动检测）
- **全局快捷键**
- **主题**（浅色 / 深色 / 跟随系统）
- **字号模式**（自动 / 适应宽度 / 自定义）

## 项目结构

```
OverlayTranslate/
├── Controls/          # 浮动工具栏、遮罩层、选区画布
├── Engines/
│   ├── Ocr/           # PaddleOCR、远程 OCR
│   └── Translation/   # Google、DeepL、百度、OpenAI、Microsoft
├── Infrastructure/    # 配置管理、快捷键、主题、托盘图标
├── Services/          # 截图、图像处理、样式分析
├── Themes/            # 浅色和深色主题资源
└── Windows/           # 覆盖窗口、设置窗口
```

## 许可证

本项目仅供个人和学习使用。

# OverlayTranslate

Windows 截图原位翻译工具。启动后可直接进入全屏截图覆盖层，框选屏幕区域，识别其中的文字并把译文原位绘制回截图覆盖层中，尽量保留原布局观感。

## 当前状态

这是一个可运行的 MVP，当前已经实现：

- Windows 托盘常驻
- 启动后可直接进入截图流程
- 全屏截图覆盖层与灰色遮罩
- 鼠标左键矩形框选
- `Esc` / 右键退出本次截图流程
- 选区 OCR 识别
- 选区文本翻译
- 选区内原位覆盖回绘
- 浮动工具栏：`重新翻译` / `重选` / `退出`
- 设置页：源语言、目标语言、OCR 策略、翻译策略、在线端点、启动即截图
- 处理阶段日志输出

## 技术栈

- `.NET 10` + `WPF`
- 托盘：`H.NotifyIcon.Wpf`
- OCR：`PaddleOCRSharp` + `Paddle.Runtime.win_x64`
- 离线翻译：`Argos Translate`
- 翻译侧车：Python HTTP 脚本

## 目录说明

- `OverlayTranslate/`: 主程序
- `OverlayTranslate/Services/`: 截图、OCR、翻译、回绘、日志等服务
- `OverlayTranslate/Models/`: 配置与数据模型
- `OverlayTranslate/Sidecar/translator_sidecar.py`: 本地翻译侧车

## 运行要求

- Windows 10/11 x64
- .NET 10 SDK 或对应运行时
- Python 3.10+

## 本地开发

### 1. 还原并编译

```powershell
dotnet build .\OverlayTranslate\OverlayTranslate.csproj
```

### 2. 安装本地翻译侧车依赖

```powershell
python -m pip install -r .\OverlayTranslate\Sidecar\requirements.txt
```

### 3. 安装 Argos Translate 语言包

当前默认离线翻译要靠 Argos 语言模型。至少需要安装你要用到的语言对，例如英文到中文：

```powershell
@'
import argostranslate.package
argostranslate.package.update_package_index()
packages = argostranslate.package.get_available_packages()
pkg = next(p for p in packages if p.from_code == "en" and p.to_code == "zh")
path = pkg.download()
argostranslate.package.install_from_path(path)
print("installed", path)
'@ | python -
```

如果你还需要 `ja -> zh`、`en -> ja` 等语言对，也要额外安装对应模型。

### 4. 启动程序

```powershell
.\OverlayTranslate\bin\Debug\net10.0-windows10.0.17763.0\OverlayTranslate.exe
```

## 使用方式

### 启动后

- 程序会创建托盘图标
- 若设置中开启“启动后立即进入截图流程”，会自动弹出覆盖层

### 托盘

- 左键：开始一次新的截图翻译
- 右键：打开菜单
- 菜单项：`开始截图翻译` / `设置` / `退出`

### 覆盖层

- 左键拖拽：选择翻译区域
- `Esc`：退出本次流程
- 右键：退出本次流程

### 结果阶段

- `重新翻译`
- `重选`
- `退出`

## 日志

程序会把关键阶段写入本地日志，包括：

- 应用启动与退出
- 截图与选区
- OCR worker 启动与结束
- 翻译请求与侧车健康检查
- 回绘与异常

日志目录：

```text
%LocalAppData%\OverlayTranslate\logs\
```

按天生成，例如：

```text
%LocalAppData%\OverlayTranslate\logs\20260428.log
```

## 已知限制

- 当前只支持主显示器
- 仅支持 Windows
- Argos 离线翻译质量受语言包限制，复杂文本效果一般
- 当前样式复刻还是 MVP 级近似实现，不是精确还原
- 未做复杂背景修补，只做文字区域近似覆盖
- 在线 OCR 尚未接入

## 常见问题

### 1. 翻译阶段报 `HttpRequestException`

通常是本地翻译侧车不可用，优先检查：

- 是否已安装 `requirements.txt`
- 是否已安装 Argos 语言包
- `7860` 端口是否被异常残留进程占用
- 日志中是否有 `Sidecar` 相关错误

### 2. OCR worker 启动失败

优先检查：

- 是否使用 x64 Windows
- 是否从编译输出目录启动程序
- 输出目录中是否包含 `PaddleOCR.dll`、`paddle_inference.dll` 等运行时文件

### 3. 日志里显示未识别到文字

这通常意味着：

- 选区过小
- 图像分辨率过低
- 文本对比度不足

## 后续可改进方向

- 多屏支持
- 更好的文字区域背景修补
- 更多语言包和翻译策略
- 在线 OCR / 在线翻译 Provider
- 更自然的字体样式复刻
- 打包安装器与首次运行引导

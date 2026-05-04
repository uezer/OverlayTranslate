# MVVM 重构设计规格

## 目标

将 OverlayTranslate 从 code-behind 驱动重构为 CommunityToolkit.Mvvm 模式，分 3 批执行，每批独立可编译。

## 技术栈

- CommunityToolkit.Mvvm 8.x（ObservableObject、RelayCommand、[ObservableProperty] 源生成器、IMessenger）
- Microsoft.Extensions.DependencyInjection（保持不变）
- WPF（.NET 10）

## 架构方案

采用**中间方案**：ViewModel 管状态 + 管线，View 用 AttachedProperty 封装 Canvas 操作，SettingsWindow 全量 MVVM 化。

---

## 第一批：提取 TranslationPipeline + OverlayWindowViewModel

### 新增文件

**`Services/TranslationPipeline.cs`**
- 职责：OCR → 翻译 → 背景填充的完整管线编排
- 依赖注入：ScreenshotService、ImageProcessor、StyleAnalyzer、ConfigManager、引擎字典
- 对外暴露：
  ```csharp
  public class TranslationPipeline
  {
      // 执行完整管线：截图裁剪 → OCR → 翻译 → 背景填充
      Task<PipelineResult> ExecuteAsync(byte[] screenshotData, Rect selection,
          double screenshotDpiX, double screenshotDpiY,
          string ocrEngineName, string translationEngineName,
          string sourceLang, string targetLang,
          CancellationToken ct);

      // 仅翻译（保留 OCR 结果，重新翻译）
      Task<PipelineResult> ReTranslateAsync(byte[] screenshotData, Rect selection,
          IReadOnlyList<TextBlock> ocrBlocks, string sourceLang, string targetLang,
          string translationEngineName, CancellationToken ct);

      // 获取当前可用引擎（消除与 DI 的重复逻辑）
      IOcrEngine GetOcrEngine(string name);
      ITranslationEngine GetTranslationEngine(string name);
  }

  public class PipelineResult
  {
      string OriginalText;
      string TranslatedText;
      IReadOnlyList<TextBlock> OcrBlocks;
      IReadOnlyList<(string Text, Rect BoundingBox)> TranslatedBlocks;
      byte[]? FilledImageBytes;
      TextStyleInfo OriginalStyle;
      TextStyleInfo TranslatedStyle;
  }
  ```
- 将 OverlayWindow.ProcessSelectionAsync()（264-376行）和 ReTranslateAsync()（525-580行）的核心逻辑提取到这里
- 引擎辅助方法（GetCurrentOcrEngine/GetCurrentTranslationEngine）也移入，消除重复

**`ViewModels/OverlayWindowViewModel.cs`**
- 继承 ObservableObject
- 职责：覆盖窗口的状态管理（不包含 UI 操作）
- 核心属性（用 [ObservableProperty] 生成）：
  ```csharp
  [ObservableProperty] private OverlayState _state = OverlayState.Idle;
  [ObservableProperty] private OverlayViewMode _viewMode = OverlayViewMode.OriginalText;
  [ObservableProperty] private bool _isLoading;
  [ObservableProperty] private BitmapImage? _backgroundSource;
  [ObservableProperty] private bool _toolbarVisible;
  [ObservableProperty] private string _originalText = "";
  [ObservableProperty] private string _translatedText = "";
  ```
- 缓存字段（普通属性，不需要通知）：
  ```csharp
  byte[]? ScreenshotData;
  IReadOnlyList<TextBlock>? LastOcrTextBlocks;
  IReadOnlyList<(string Text, Rect BoundingBox)>? TranslatedBlocks;
  byte[]? FilledImageBytes;
  TextStyleInfo? OriginalStyle;
  TextStyleInfo? TranslatedStyle;
  Rect CurrentSelection;
  double ScreenshotDpiX, ScreenshotDpiY;
  string CurrentOcrEngineName, CurrentTranslationEngineName;
  CancellationTokenSource Cts;
  ```
- 命令：
  ```csharp
  [RelayCommand] private Task ProcessSelectionAsync(Rect selection);
  [RelayCommand] private Task ReTranslateAsync();
  [RelayCommand] private void RerunAll();
  [RelayCommand] private void RerunTranslation();
  [RelayCommand] private void HandleReselect();
  [RelayCommand] private void ExitOverlay();
  ```
- 对话工具栏的引擎/语言切换通过 Messenger（ValueChangedMessage）或直接暴露属性

### 修改文件

**`Windows/OverlayWindow.xaml.cs`**
- 从 722 行缩减到约 200 行
- 保留：鼠标选区事件（OnMouseLeftButtonDown/Move/Up）、键盘/右键退出、TextBox 覆盖创建和定位、工具栏定位
- 移除：所有管线逻辑（→ TranslationPipeline）、所有状态字段（→ ViewModel）、引擎辅助方法（→ TranslationPipeline）、显示名映射（→ 翻译引擎字典 key 改用显示名或移到 ViewModel）
- 构造函数注入 ViewModel（通过 DI 工厂创建）
- 用 DataContext 绑定部分属性（Loading 状态、背景图等）

**`Windows/OverlayWindow.xaml`**
- 无结构性变更。可能添加对 ViewModel 属性的绑定（如 Loading 状态到 ProgressBar 的绑定）

**`App.xaml.cs`**
- 注册 TranslationPipeline 为 Singleton
- 注册 OverlayWindowViewModel 为 Transient（跟随 OverlayWindow）
- 移除 TextRenderer 注册
- 移除 PythonRuntime 和 PythonBridge 注册

**`OverlayTranslate.csproj`**
- 添加 CommunityToolkit.Mvvm NuGet 包引用

### 需删除的代码

- `Services/TextRenderer.cs` — 已被 TextBox 覆盖方案替代，从未被调用
- `Python/PythonRuntime.cs`、`Python/PythonBridge.cs` — 注册了但从未被任何组件使用
- OverlayWindow 中的引擎显示名映射方法（MapOcrDisplayName 等 4 个静态方法）— 简化为直接用引擎 Name 属性

---

## 第二批：SettingsWindow MVVM 化

### 新增文件

**`ViewModels/SettingsViewModel.cs`**
- 继承 ObservableObject
- 每个设置字段一个 [ObservableProperty]
- 构造函数注入 ConfigManager + 引擎字典
- [RelayCommand] SaveCommand — 保存设置、应用主题、关闭窗口
- 将 LoadSettings() 的手动读取逻辑移到构造函数（属性初始化）
- 将 OnSaveClick() 的手动写入逻辑移到 SaveCommand

XAML 绑定改造：
- 所有 `x:Name` 控件 → `{Binding PropertyName}`
- `Click="OnSaveClick"` → `Command="{Binding SaveCommand}"`
- ComboBox 的 ItemsSource 用绑定代替手动 Add

### 修改文件

**`Windows/SettingsWindow.xaml`**
- 添加 DataContext（通过 XAML 或 code-behind 注入 ViewModel）
- 移除所有 x:Name，改用 Binding
- Button 的 Click → Command

**`Windows/SettingsWindow.xaml.cs`**
- 从 177 行缩减到约 30 行（仅 InitializeComponent + DataContext 设置）

---

## 第三批：死代码清理 + FloatingToolbar 命令化

### FloatingToolbar 改造

**修改 `Controls/FloatingToolbar.xaml.cs`**
- 将 9 个 raw event 替换为 [RelayCommand] 或直接绑定
- 保留拖拽逻辑（纯 View 层操作，不需要 ViewModel）
- 引擎/语言切换事件 → 通过 IMessenger 发送消息，OverlayWindowViewModel 订阅
- 或更简单：保留事件但用 RelayCommand 替代 Click handler

### 显示名映射

当前问题：引擎字典 key 是内部名称（"Baidu"），显示名在 OverlayWindow 中硬编码映射（"Baidu" → "百度"）。

方案：在引擎字典注册时就用显示名作为 key，或让 ITranslationEngine/IOcrEngine 接口增加 DisplayName 属性。推荐前者（改动最小）：
```csharp
// App.xaml.cs 中注册引擎字典时 key 改用显示名
["百度"] = sp.GetRequiredService<BaiduTranslationEngine>(),
```
这样就不需要 Map/Unmap 方法了。

### 清理

- 删除 `Services/TextRenderer.cs`
- 删除 `Python/PythonRuntime.cs`、`Python/PythonBridge.cs`
- 删除 `Python/` 目录
- 从 csproj 移除 pythonnet 包引用
- 从 App.xaml.cs 移除所有 Python 和 TextRenderer 注册

---

## 文件变更总览

| 操作 | 文件 |
|------|------|
| 新增 | `Services/TranslationPipeline.cs` |
| 新增 | `ViewModels/OverlayWindowViewModel.cs` |
| 新增 | `ViewModels/SettingsViewModel.cs` |
| 修改 | `OverlayTranslate.csproj`（添加 CommunityToolkit.Mvvm） |
| 修改 | `Windows/OverlayWindow.xaml.cs`（从 722 行缩减到 ~200 行） |
| 修改 | `Windows/OverlayWindow.xaml`（可能添加绑定） |
| 修改 | `Windows/SettingsWindow.xaml`（改用 Binding） |
| 修改 | `Windows/SettingsWindow.xaml.cs`（从 177 行缩减到 ~30 行） |
| 修改 | `Controls/FloatingToolbar.xaml.cs`（事件 → 命令） |
| 修改 | `Controls/FloatingToolbar.xaml`（可能添加 Command 绑定） |
| 修改 | `App.xaml.cs`（注册新服务，清理死代码） |
| 删除 | `Services/TextRenderer.cs` |
| 删除 | `Python/PythonRuntime.cs` |
| 删除 | `Python/PythonBridge.cs` |

## 验证标准

1. 构建通过，无编译错误
2. 现有单元测试全部通过
3. 截图 → OCR → 翻译 → 覆盖显示 完整流程正常
4. 工具栏引擎/语言切换后重新翻译正常
5. 视图切换（原文/译文/原图）正常
6. 设置窗口打开、修改、保存正常
7. 托盘图标、热键、退出正常

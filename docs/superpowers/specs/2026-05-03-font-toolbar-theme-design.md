# 字体大小、工具栏避让、暗黑模式 设计规格

## 概述

三个功能：字体大小策略（匹配原图/自适应/自定义）、工具栏自动避让+可拖动、暗黑模式（浅色/深色/跟随系统）。

---

## 功能 1：字体大小策略

### 需求

- 默认：匹配原图文字大小（用 OCR bounding box 高度估算）
- 可选：自适应选区宽度（确保译文不超出选区）
- 可选：用户自定义固定字号
- 设置中可切换模式

### 数据流

```
OCR 返回 TextBlocks[].BoundingBox.Height
  → 取中位数作为 baseFontSize
  → 传给 StyleAnalyzer.Analyze(selection, text, baseFontSize, mode)
  → mode=auto: 使用 baseFontSize
  → mode=fit-width: 根据选区宽度和译文长度缩放
  → mode=custom: 使用 settings.Other.CustomFontSize
```

### 修改文件

- `Models/AppSettings.cs` — 新增 `Other.FontSizeMode`（string: "auto"/"fit-width"/"custom"）、`Other.CustomFontSize`（int, 默认 14）
- `Services/StyleAnalyzer.cs` — `Analyze()` 新增参数，根据 mode 计算字号
- `Windows/OverlayWindow.xaml.cs` — 从 OCR 结果提取字号，传给 StyleAnalyzer
- `Windows/SettingsWindow.xaml` — "其他" tab 新增字号模式和自定义字号设置
- `Windows/SettingsWindow.xaml.cs` — 读写字号设置
- `Engines/Ocr/PaddleOcrEngine.cs` — 确保 BoundingBox 正确传递
- `Engines/IOcrEngine.cs` — TextBlock 模型需包含 BoundingBox

### OCR TextBlock 扩展

当前 `OcrResult.TextBlock` 只有 `Text` 属性。需要新增 `BoundingBox`（Rect 类型），用于估算原图字号。

---

## 功能 2：工具栏自动避让 + 可拖动

### 需求

- 自动避让：选区靠近屏幕边缘时，工具栏自动调整位置
- 可拖动：用户可拖动工具栏到任意位置
- 重选时恢复自动避让

### 自动避让算法

```
优先级: 下 → 上 → 右 → 左 → 贴边

1. y = selection.Bottom + 8（下方）
   if y + toolbarHeight > screenHeight → y = selection.Top - toolbarHeight - 8（上方）
   if y < 0 → 尝试横向

2. x = selection.Right + 8（右侧）
   if x + toolbarWidth > screenWidth → x = selection.Left - toolbarWidth - 8（左侧）
   if x < 0 → clamp 到屏幕边缘

3. 最终 clamp: x = max(8, min(x, screenWidth - toolbarWidth - 8))
                y = max(8, min(y, screenHeight - toolbarHeight - 8))
```

### 可拖动实现

- FloatingToolbar 的 Border 添加 `MouseLeftButtonDown` 开始拖动
- `MouseMove` 更新 `Canvas.SetLeft/SetTop`
- `MouseLeftButtonDown` 时设置 `_isDragging = true`，`MouseLeftUp` 时 `_isDragging = false`
- 拖动后 `_autoPosition = false`（停止自动避让）
- 重选时 `_autoPosition = true`（恢复自动避让）

### 修改文件

- `Windows/OverlayWindow.xaml.cs` — 重写 `PositionToolbar()` 方法
- `Controls/FloatingToolbar.xaml.cs` — 添加拖动事件处理
- `Controls/FloatingToolbar.xaml` — Border 添加 Cursor="SizeAll"

---

## 功能 3：暗黑模式

### 需求

- 设置中可选：浅色 / 深色 / 跟随系统
- 影响：FloatingToolbar、SettingsWindow、托盘菜单
- 不影响：覆盖层遮罩（始终半透明黑色）

### 主题资源

创建两套颜色资源字典：

**Light.xaml：**
- WindowBackground: #FFFFFF
- PanelBackground: #F0F0F0
- BorderBrush: #CCCCCC
- TextColor: #1E1E1E
- AccentColor: #0078D4

**Dark.xaml：**
- WindowBackground: #1E1E1E
- PanelBackground: #2D2D2D
- BorderBrush: #404040
- TextColor: #E0E0E0
- AccentColor: #4CC2FF

### 跟随系统检测

P/Invoke `DwmGetWindowAttribute` 检测系统深色模式，或读取注册表：
```
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
AppsUseLightTheme (DWORD): 0=深色, 1=浅色
```

### 主题切换机制

```
ThemeManager.SetTheme("dark" | "light" | "system")
  → 如果 "system"，读取当前系统主题
  → Application.Current.Resources.MergedDictionaries[0] = 新主题字典
  → 触发 ThemeChanged 事件
  → 各组件响应事件更新样式
```

### 修改文件

- 新建 `Themes/Light.xaml`、`Themes/Dark.xaml` — 颜色资源字典
- 新建 `Infrastructure/ThemeManager.cs` — 主题管理
- `Models/AppSettings.cs` — 新增 `Other.Theme`（string: "light"/"dark"/"system"）
- `Controls/FloatingToolbar.xaml` — 使用 DynamicResource 绑定颜色
- `Windows/SettingsWindow.xaml` — 使用 DynamicResource 绑定颜色
- `Windows/SettingsWindow.xaml.cs` — "其他" tab 新增主题选择
- `App.xaml.cs` — 初始化 ThemeManager

---

## 测试计划

- 字体大小：三种模式的字号计算正确性测试
- 工具栏避让：各种屏幕边缘位置的计算测试
- 暗黑模式：主题切换后颜色正确性测试
- 集成测试：设置保存/加载/生效

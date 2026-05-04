# 安装包打包设计规格

## 目标

为 OverlayTranslate 添加 Inno Setup 安装包打包能力，支持本地一键构建和 GitHub Actions 自动发布。

## 决策

| 决策 | 选择 |
|------|------|
| 安装包格式 | Inno Setup (.iss) |
| 发布模式 | Framework-Dependent（依赖 .NET 10 运行时） |
| 构建方式 | 本地 PowerShell 脚本 + GitHub Actions CI/CD |
| 版本号 | 手动在 csproj 中维护 |
| 架构 | x64（native 依赖 Paddle.Runtime.win_x64 限制） |

## 技术栈

- Inno Setup 6.x（免费开源安装包制作工具）
- PowerShell 7+（本地构建脚本）
- GitHub Actions（CI/CD 自动发布）
- dotnet publish（.NET 发布命令）

---

## 新增文件

### `installer/OverlayTranslate.iss`

Inno Setup 脚本，核心配置：

```iss
#define MyAppName "OverlayTranslate"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Ezer"
#define MyAppURL "https://github.com/Ezer/OverlayTranslate"
#define MyAppExeName "OverlayTranslate.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=OverlayTranslate-{#MyAppVersion}-setup
SetupIconFile=..\OverlayTranslate\Assets\app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "..\OverlayTranslate\bin\Release\net10.0-windows\win-x64\publish\*"; \
  DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
  Flags: nowait postinstall skipifsilent
```

关键点：
- `Source` 指向 dotnet publish 的输出目录
- `ArchitecturesAllowed=x64` 因为 native DLL 只有 x64 版本
- 安装后可选启动应用
- 默认创建桌面快捷方式

### `build.ps1`

根目录 PowerShell 构建脚本，一键构建安装包：

```powershell
param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$projectDir = "$PSScriptRoot\OverlayTranslate"
$publishDir = "$projectDir\bin\$Configuration\net10.0-windows\win-x64\publish"
$distDir = "$PSScriptRoot\dist"

# 1. 读取版本号
if (-not $Version) {
    [xml]$csproj = Get-Content "$projectDir\OverlayTranslate.csproj"
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "Building OverlayTranslate v$Version" -ForegroundColor Cyan

# 2. 清理
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $distDir -ErrorAction SilentlyContinue

# 3. dotnet publish
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish "$projectDir\OverlayTranslate.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# 4. Inno Setup 编译
Write-Host "Compiling installer..." -ForegroundColor Cyan
$issc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $issc)) {
    $issc = "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
}
if (-not (Test-Path $issc)) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php"
}

& $issc "$PSScriptRoot\installer\OverlayTranslate.iss" /DMyAppVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

Write-Host "Done! Installer: dist\OverlayTranslate-$Version-setup.exe" -ForegroundColor Green
```

用法：`.\build.ps1` 或 `.\build.ps1 -Version "1.2.0"`

### `.github/workflows/release.yml`

GitHub Actions 工作流，push tag `v*` 时自动构建并发布：

```yaml
name: Release

on:
  push:
    tags: ['v*']

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Extract version from tag
        id: version
        shell: pwsh
        run: |
          $tag = "${{ github.ref_name }}"
          $version = $tag -replace '^v', ''
          echo "version=$version" >> $env:GITHUB_OUTPUT

      - name: Publish
        run: >
          dotnet publish OverlayTranslate\OverlayTranslate.csproj
          -c Release
          -r win-x64
          --self-contained false
          -o OverlayTranslate\bin\Release\net10.0-windows\win-x64\publish

      - name: Install Inno Setup
        run: choco install innosetup -y

      - name: Compile installer
        shell: pwsh
        run: >
          & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
          installer\OverlayTranslate.iss
          /DMyAppVersion=${{ steps.version.outputs.version }}

      - name: Create Release
        uses: softprops/action-gh-release@v2
        with:
          files: dist/*.exe
          generate_release_notes: true
```

用法：`git tag v1.0.0 && git push origin v1.0.0`

---

## 修改文件

### `OverlayTranslate/OverlayTranslate.csproj`

添加打包元数据：

```xml
<PropertyGroup>
  <!-- 现有属性保持不变 -->
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0.0</AssemblyVersion>
  <Description>Screen region OCR and translation overlay tool</Description>
</PropertyGroup>
```

### `README.md` / `README.zh-CN.md`

添加"安装"章节：

```markdown
## 安装

从 [Releases](https://github.com/Ezer/OverlayTranslate/releases) 下载最新版本的安装包。

**要求：** Windows 10/11 x64 + [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)

### 从源码构建

\`\`\`powershell
# 开发运行
dotnet run --project OverlayTranslate

# 打包安装包（需要安装 Inno Setup 6）
.\build.ps1
\`\`\`
```

---

## 文件变更总览

| 操作 | 文件 |
|------|------|
| 新增 | `installer/OverlayTranslate.iss` |
| 新增 | `build.ps1` |
| 新增 | `.github/workflows/release.yml` |
| 修改 | `OverlayTranslate/OverlayTranslate.csproj`（加 Version 等） |
| 修改 | `README.md`（加安装说明） |
| 修改 | `README.zh-CN.md`（加安装说明） |

## 验证标准

1. `.\build.ps1` 成功运行，`dist/` 目录下生成 `.exe` 安装包
2. 安装包在干净的 Windows x64 机器上安装成功（需预装 .NET 10）
3. 安装后桌面快捷方式可以正常启动应用
4. 卸载后应用文件被正确清理
5. push `v*` tag 后 GitHub Actions 成功构建并上传 Release

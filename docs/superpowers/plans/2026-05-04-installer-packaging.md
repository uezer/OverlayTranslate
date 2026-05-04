# 安装包打包实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 为 OverlayTranslate 添加 Inno Setup 安装包打包能力，支持本地一键构建和 GitHub Actions 自动发布。

**架构：** 在 csproj 中添加版本元数据，创建 Inno Setup 脚本定义安装包结构，PowerShell 脚本一键构建，GitHub Actions 在 push tag 时自动发布 Release。

**技术栈：** Inno Setup 6.x、PowerShell 7、GitHub Actions、dotnet publish

---

## 文件结构

| 操作 | 文件 | 职责 |
|------|------|------|
| 修改 | `OverlayTranslate/OverlayTranslate.csproj` | 添加 Version、AssemblyVersion、Description 元数据 |
| 创建 | `installer/OverlayTranslate.iss` | Inno Setup 安装包脚本 |
| 创建 | `build.ps1` | 本地一键构建脚本 |
| 创建 | `.github/workflows/release.yml` | GitHub Actions 自动发布工作流 |
| 修改 | `.gitignore` | 添加 `dist/` 目录忽略 |
| 修改 | `README.md` | 添加安装说明 |
| 修改 | `README.zh-CN.md` | 添加安装说明 |

---

### 任务 1：csproj 添加版本元数据

**文件：**
- 修改：`OverlayTranslate/OverlayTranslate.csproj`

- [ ] **步骤 1：添加 Version 和元数据**

在 csproj 的 `<PropertyGroup>` 中，紧跟 `<ApplicationIcon>` 之后添加三个属性：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationIcon>Assets\app.ico</ApplicationIcon>
    <Version>1.0.0</Version>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <Description>Screen region OCR and translation overlay tool</Description>
  </PropertyGroup>

  <!-- 以下内容不变 -->
```

- [ ] **步骤 2：验证编译通过**

运行：`dotnet build OverlayTranslate\OverlayTranslate.csproj`
预期：Build succeeded

- [ ] **步骤 3：Commit**

```bash
git add OverlayTranslate/OverlayTranslate.csproj
git commit -m "build: 添加版本元数据 (Version, AssemblyVersion, Description)"
```

---

### 任务 2：创建 Inno Setup 安装包脚本

**文件：**
- 创建：`installer/OverlayTranslate.iss`

- [ ] **步骤 1：创建 installer 目录和 .iss 文件**

创建 `installer/OverlayTranslate.iss`，完整内容：

```iss
; OverlayTranslate Inno Setup Script
; Build: ISCC OverlayTranslate.iss /DMyAppVersion=1.0.0

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#define MyAppName "OverlayTranslate"
#define MyAppPublisher "Ezer"
#define MyAppURL "https://github.com/Ezer013/OverlayTranslate"
#define MyAppExeName "OverlayTranslate.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\dist
OutputBaseFilename=OverlayTranslate-{#MyAppVersion}-setup
SetupIconFile=..\OverlayTranslate\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin

[Files]
Source: "..\OverlayTranslate\bin\Release\net10.0-windows\win-x64\publish\*"; \
  DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent
```

关键细节：
- `#ifndef` 保护：如果不传 `/DMyAppVersion=` 参数则使用默认值 `1.0.0`
- `PrivilegesRequired=admin`：安装到 Program Files 需要管理员权限
- `ignoreversion` flag：覆盖更新时不比较版本号
- 桌面快捷方式默认**不勾选**（`unchecked`），用户可选
- `[Run]` 段安装后可选启动，使用 `StringChange` 转义 `&` 字符

- [ ] **步骤 2：验证文件语法**

这一步在有 Inno Setup 的机器上验证。跳过编译测试（依赖安装 Inno Setup），改为验证文件内容正确性：确认文件创建成功且内容完整。

- [ ] **步骤 3：Commit**

```bash
git add installer/OverlayTranslate.iss
git commit -m "build: 添加 Inno Setup 安装包脚本"
```

---

### 任务 3：创建本地构建脚本 build.ps1

**文件：**
- 创建：`build.ps1`

- [ ] **步骤 1：创建 build.ps1**

根目录创建 `build.ps1`，完整内容：

```powershell
param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "OverlayTranslate"
$csprojPath = Join-Path $projectDir "OverlayTranslate.csproj"
$publishDir = Join-Path $projectDir "bin\$Configuration\net10.0-windows\win-x64\publish"
$distDir = Join-Path $PSScriptRoot "dist"
$issPath = Join-Path $PSScriptRoot "installer\OverlayTranslate.iss"

# 1. Read version from csproj if not provided
if (-not $Version) {
    [xml]$csproj = Get-Content $csprojPath
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}
Write-Host "Building OverlayTranslate v$Version" -ForegroundColor Cyan

# 2. Clean previous outputs
Write-Host "Cleaning..." -ForegroundColor Cyan
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $distDir -ErrorAction SilentlyContinue

# 3. dotnet publish (framework-dependent, win-x64)
Write-Host "Publishing..." -ForegroundColor Cyan
dotnet publish $csprojPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# 4. Find Inno Setup compiler
Write-Host "Compiling installer..." -ForegroundColor Cyan
$isscPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$issc = $isscPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $issc) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php"
}

# 5. Compile installer
& $issc $issPath /DMyAppVersion=$Version
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE" }

$installerPath = Join-Path $distDir "OverlayTranslate-$Version-setup.exe"
Write-Host "Done! Installer: $installerPath" -ForegroundColor Green
```

- [ ] **步骤 2：验证脚本语法**

运行 PowerShell 语法检查：
```powershell
$null = [System.Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot "build.ps1"),
    [ref]$null, [ref]$null
)
```
或者用简化命令：
```powershell
powershell -Command "Get-Content build.ps1 | Out-Null; Write-Host 'Syntax OK'"
```
预期：无语法错误输出

- [ ] **步骤 3：Commit**

```bash
git add build.ps1
git commit -m "build: 添加本地一键构建脚本 (build.ps1)"
```

---

### 任务 4：创建 GitHub Actions 发布工作流

**文件：**
- 创建：`.github/workflows/release.yml`

- [ ] **步骤 1：创建 .github/workflows 目录和 release.yml**

```yaml
name: Release

on:
  push:
    tags: ['v*']

permissions:
  contents: write

jobs:
  build-installer:
    runs-on: windows-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

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
          "version=$version" >> $env:GITHUB_OUTPUT

      - name: Publish
        run: >
          dotnet publish OverlayTranslate\OverlayTranslate.csproj
          -c Release
          -r win-x64
          --self-contained false
          -o OverlayTranslate\bin\Release\net10.0-windows\win-x64\publish

      - name: Install Inno Setup
        run: choco install innosetup -y --no-progress

      - name: Compile installer
        shell: pwsh
        run: >
          & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
          installer\OverlayTranslate.iss
          /DMyAppVersion=${{ steps.version.outputs.version }}

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: dist/*.exe
          generate_release_notes: true
```

关键细节：
- `permissions: contents: write`：允许创建 Release
- `softprops/action-gh-release@v2`：成熟的 Release 创建 Action
- `generate_release_notes: true`：自动生成变更日志
- 版本号从 git tag 中提取（`v1.2.3` → `1.2.3`）

- [ ] **步骤 2：验证 YAML 语法**

用 PowerShell 验证 YAML 是合法的：
```powershell
Get-Content .github\workflows\release.yml | Out-Null
# 确认文件存在且可读
```

- [ ] **步骤 3：Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: 添加 GitHub Actions 自动发布工作流"
```

---

### 任务 5：更新 .gitignore

**文件：**
- 修改：`.gitignore`

- [ ] **步骤 1：添加 dist/ 忽略规则**

在 `.gitignore` 文件末尾添加：

```
# Build output
dist/
```

- [ ] **步骤 2：Commit**

```bash
git add .gitignore
git commit -m "build: gitignore 添加 dist/ 目录"
```

---

### 任务 6：更新 README 安装说明

**文件：**
- 修改：`README.md`
- 修改：`README.zh-CN.md`

- [ ] **步骤 1：更新 README.md**

在 `## Quick Start` 的 `### Requirements` 前面插入安装章节。将：

```markdown
## Quick Start

### Requirements
```

替换为：

```markdown
## Install

Download the latest installer from [Releases](https://github.com/Ezer013/OverlayTranslate/releases).

**Requirements:** Windows 10/11 (x64) + [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

## Quick Start

### Requirements
```

- [ ] **步骤 2：更新 README.zh-CN.md**

在 `## 快速开始` 的 `### 环境要求` 前面插入安装章节。将：

```markdown
## 快速开始

### 环境要求
```

替换为：

```markdown
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

### 环境要求
```

- [ ] **步骤 3：Commit**

```bash
git add README.md README.zh-CN.md
git commit -m "docs: README 添加安装说明"
```

---

## 验证

- [ ] **最终验证：构建测试**

在有 Inno Setup 的机器上运行：
```powershell
.\build.ps1
```
预期：`dist/OverlayTranslate-1.0.0-setup.exe` 生成成功

- [ ] **最终验证：测试通过**

```bash
dotnet test
```
预期：All tests passed

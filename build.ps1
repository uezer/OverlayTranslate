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
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
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

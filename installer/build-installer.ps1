#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes WinCleaner (framework-dependent) and compiles the Inno Setup installer.
.NOTES
  Requires Inno Setup 6 (ISCC.exe) and .NET 8 SDK.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Root) { $Root = (Resolve-Path "$PSScriptRoot\..").Path }

$Project = Join-Path $Root "src\WinCleaner\WinCleaner.csproj"
$PublishDir = Join-Path $Root "publish\installer"
$SetupOut = Join-Path $Root "publish\setup"
$Iss = Join-Path $Root "installer\WinCleaner.iss"
$IconSrc = Join-Path $Root "src\WinCleaner\app.ico"
$IconDst = Join-Path $Root "installer\app.ico"

$IsccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isinfo.php"
}

if (-not (Test-Path $IconSrc)) {
    throw "Missing app icon: $IconSrc"
}
Copy-Item $IconSrc $IconDst -Force

Write-Host "==> Syncing SOURCE_CODES snapshot..." -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $Root "scripts\sync-source-codes.ps1")
if ($LASTEXITCODE -ne 0) { throw "sync-source-codes failed ($LASTEXITCODE)" }

Write-Host "==> Publishing framework-dependent $Runtime build..." -ForegroundColor Cyan
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

dotnet publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Get-ChildItem $PublishDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

# Bundle open-source tree next to the app payload for the installer
$SourceCodes = Join-Path $Root "SOURCE_CODES"
$PublishSource = Join-Path $PublishDir "SOURCE_CODES"
if (Test-Path $PublishSource) { Remove-Item $PublishSource -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishSource | Out-Null
& robocopy $SourceCodes $PublishSource /E /NFL /NDL /NJH /NJS /nc /ns /np /XD bin obj .vs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Failed to copy SOURCE_CODES into publish payload" }
$global:LASTEXITCODE = 0

$exe = Join-Path $PublishDir "WinCleaner.exe"
if (-not (Test-Path $exe)) { throw "Publish output missing WinCleaner.exe" }

Write-Host "==> Compiling Inno Setup..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $SetupOut | Out-Null

& $Iscc `
    "/DPublishDir=$PublishDir" `
    "/DOutputDir=$SetupOut" `
    "/DMyAppVersion=$Version" `
    $Iss

if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Get-ChildItem $SetupOut -Filter "WinCleaner-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "Setup ready:" -ForegroundColor Green
Write-Host "  $($setup.FullName)"
Write-Host "  Size: $([math]::Round($setup.Length / 1MB, 1)) MB"
Write-Host ""
Write-Host "Installer downloads missing deps at install time:"
Write-Host "  - .NET 8 Desktop Runtime (x64)"
Write-Host "  - VC++ 2015-2022 Redistributable (x64)"
Write-Host "Updates: https://github.com/emirttac/WinCleaner/releases"

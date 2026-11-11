#Requires -Version 5.1
<#
.SYNOPSIS
  Builds a single portable WinCleaner.exe (self-contained, win-x64).
  No separate runtime install or app\ folder required.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "2.6.0"
)

$ErrorActionPreference = "Stop"
if ($Runtime -eq "win-arm64") {
    throw "win-arm64 is not published. There is no GitHub windows-11-arm runner and no documented VM. The x64 build stays. An ARM64 operating system still asks before a system write."
}
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Root) { $Root = (Resolve-Path "$PSScriptRoot\..").Path }

$Out = Join-Path $Root "publish"
$Staging = Join-Path $Root "publish\_staging"
$AppProj = Join-Path $Root "src\WinCleaner\WinCleaner.csproj"
$FinalExe = Join-Path $Out "WinCleaner.exe"

Write-Host "==> Publishing self-contained single-file WinCleaner" -ForegroundColor Cyan
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Staging | Out-Null

dotnet publish $AppProj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $Staging
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$built = Join-Path $Staging "WinCleaner.exe"
if (-not (Test-Path $built)) { throw "Missing output: $built" }

# Replace only the portable exe at publish root (keep other folders like installer/ if present)
New-Item -ItemType Directory -Force -Path $Out | Out-Null
Copy-Item $built $FinalExe -Force

# Remove leftover sidecar files from a previous multi-file layout
$legacyApp = Join-Path $Out "app"
if (Test-Path $legacyApp) { Remove-Item $legacyApp -Recurse -Force }

Remove-Item $Staging -Recurse -Force

$sizeMb = [math]::Round((Get-Item $FinalExe).Length / 1MB, 1)
Write-Host ""
Write-Host "OK" -ForegroundColor Green
Write-Host "  $FinalExe  ($sizeMb MB)"
Write-Host "  Single exe - run as administrator. Checks OS, administrator, and the VC++ runtime key on startup."
Write-Host "  No NuGet package references a Visual C++ library. The framework-dependent publish has no vcruntime DLL. The self-contained publish includes vcruntime140_cor3.dll from the .NET runtime. If the VC++ 2015-2022 x64 runtime key is missing, WinCleaner can still install it quietly."

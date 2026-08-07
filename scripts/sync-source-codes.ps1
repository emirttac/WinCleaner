#Requires -Version 5.1
<#
.SYNOPSIS
  Syncs the open-source SOURCE_CODES snapshot from the canonical src/tests/installer trees.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Root) { $Root = (Resolve-Path "$PSScriptRoot\..").Path }

$Dest = Join-Path $Root "SOURCE_CODES"
$Xd = @('bin', 'obj', '.vs', 'TestResults')

function Sync-Tree([string]$From, [string]$To) {
    if (-not (Test-Path $From)) { throw "Missing source tree: $From" }
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    $rcArgs = @($From, $To, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/nc', '/ns', '/np', '/XD') + $Xd
    & robocopy @rcArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $From -> $To (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
}

Write-Host "==> Syncing SOURCE_CODES..." -ForegroundColor Cyan

if (Test-Path $Dest) {
    Get-ChildItem $Dest -Force | Where-Object { $_.Name -ne 'README.md' } | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
}

Sync-Tree (Join-Path $Root "src\WinCleaner") (Join-Path $Dest "src\WinCleaner")
Sync-Tree (Join-Path $Root "src\WinCleaner.Core") (Join-Path $Dest "src\WinCleaner.Core")
Sync-Tree (Join-Path $Root "src\WinCleaner.Data") (Join-Path $Dest "src\WinCleaner.Data")
Sync-Tree (Join-Path $Root "tests\WinCleaner.Core.Tests") (Join-Path $Dest "tests\WinCleaner.Core.Tests")
Sync-Tree (Join-Path $Root "installer") (Join-Path $Dest "installer")
Sync-Tree (Join-Path $Root "scripts") (Join-Path $Dest "scripts")

Copy-Item (Join-Path $Root "WinCleaner.slnx") (Join-Path $Dest "WinCleaner.slnx") -Force
Copy-Item (Join-Path $Root ".gitignore") (Join-Path $Dest ".gitignore") -Force

$license = Join-Path $Root "LICENSE"
if (Test-Path $license) {
    Copy-Item $license (Join-Path $Dest "LICENSE") -Force
}

$assetsSrc = Join-Path $Root "assets\app.ico"
if (Test-Path $assetsSrc) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Dest "assets") | Out-Null
    Copy-Item $assetsSrc (Join-Path $Dest "assets\app.ico") -Force
}

$readme = Join-Path $Dest "README.md"
if (-not (Test-Path $readme)) {
    throw "SOURCE_CODES\README.md is missing. Recreate it before syncing."
}

$files = Get-ChildItem $Dest -Recurse -File
$sizeMb = [math]::Round((($files | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host ("Done. {0} files ({1} MB)" -f $files.Count, $sizeMb) -ForegroundColor Green

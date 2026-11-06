#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes the x64 portable build, SHA256 sums, and an SPDX 2.3 file.
  win-arm64 is refused. The release is unsigned.
#>
[CmdletBinding()]
param(
    [string]$Version = "2.5.0"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
if ($Version.StartsWith("v")) { $Version = $Version.Substring(1) }

& (Join-Path $PSScriptRoot "publish-portable.ps1") -Version $Version -Runtime "win-x64"
if ($LASTEXITCODE -ne 0) { throw "portable publish failed" }

$exe = Join-Path $Root "publish\WinCleaner.exe"
$hash = (Get-FileHash -Algorithm SHA256 $exe).Hash.ToLowerInvariant()
$sums = Join-Path $Root "publish\SHA256SUMS.txt"
Set-Content -Path $sums -Encoding ascii -Value "$hash  WinCleaner.exe"

$spdx = Join-Path $Root "publish\WinCleaner.spdx.json"
$doc = @{
    spdxVersion = "SPDX-2.3"
    dataLicense = "CC0-1.0"
    SPDXID = "SPDXRef-DOCUMENT"
    name = "WinCleaner"
    documentNamespace = "https://github.com/emirttac/WinCleaner/releases/tag/v$Version"
    comment = "Unsigned release. SHA256 is the verification. dotnet sbom is not part of the .NET 8 SDK, so this file is written here."
    files = @(
        @{
            SPDXID = "SPDXRef-WinCleaner-exe"
            fileName = "./WinCleaner.exe"
            checksums = @(@{ algorithm = "SHA256"; checksumValue = $hash })
        }
    )
}
$doc | ConvertTo-Json -Depth 6 | Set-Content -Path $spdx -Encoding utf8
Write-Host "Unsigned release files:"
Write-Host "  $exe"
Write-Host "  $sums"
Write-Host "  $spdx"

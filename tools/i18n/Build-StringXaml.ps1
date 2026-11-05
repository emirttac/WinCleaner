#Requires -Version 5.1
<#
.SYNOPSIS
  Builds Strings.<lang>.xaml from tools/i18n/<lang>.json and verifies key parity.
#>
param(
    [string[]]$Languages = @('en','tr','de','es','zh','fr','ro','ru')
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root "tools\i18n\en.json"))) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$I18n = Join-Path $Root "tools\i18n"
$OutDir = Join-Path $Root "src\WinCleaner\Resources"

function Escape-Xml([string]$s) {
    if ($null -eq $s) { return "" }
    return ($s -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;' -replace '"','&quot;')
}

function Get-Keys($obj) {
    @($obj.PSObject.Properties.Name)
}

$en = Get-Content (Join-Path $I18n "en.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$enKeys = Get-Keys $en | Sort-Object
Write-Host "EN baseline keys: $($enKeys.Count)"

foreach ($lang in $Languages) {
    $jsonPath = Join-Path $I18n "$lang.json"
    if (-not (Test-Path $jsonPath)) { throw "Missing $jsonPath" }
    $data = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $keys = Get-Keys $data | Sort-Object
    $missing = Compare-Object $enKeys $keys | Where-Object SideIndicator -eq '<=' | ForEach-Object InputObject
    $extra = Compare-Object $enKeys $keys | Where-Object SideIndicator -eq '=>' | ForEach-Object InputObject
    if ($missing -or $extra) {
        throw "$lang key mismatch. missing=$($missing.Count) extra=$($extra.Count)"
    }

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"')
    [void]$sb.AppendLine('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
    [void]$sb.AppendLine('                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">')
    [void]$sb.AppendLine()

    # Preserve EN key order for readable diffs
    foreach ($k in (Get-Keys $en)) {
        $val = $data.$k
        if ($null -eq $val) { throw "$lang missing value for $k" }
        $esc = Escape-Xml ([string]$val)
        [void]$sb.AppendLine("  <sys:String x:Key=`"$k`">$esc</sys:String>")
    }

    [void]$sb.AppendLine()
    [void]$sb.AppendLine('</ResourceDictionary>')

    $out = Join-Path $OutDir "Strings.$lang.xaml"
    # UTF-8 with BOM helps WPF/MSBuild on Windows for non-ASCII
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($out, $sb.ToString(), $utf8Bom)
    Write-Host "Wrote $out ($($keys.Count) keys)"
}

Write-Host "All languages OK." -ForegroundColor Green

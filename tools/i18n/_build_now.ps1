#Requires -Version 5.1
# Regenerates Strings.*.xaml from tools/i18n/*.json (UTF-8 safe via BuildXaml).
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root "tools\i18n\en.json"))) {
    $Root = "C:\Users\emir\Desktop\WinCleaner"
}
dotnet run --project (Join-Path $Root "tools\i18n\BuildXaml\BuildXaml.csproj") -c Release -- $Root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

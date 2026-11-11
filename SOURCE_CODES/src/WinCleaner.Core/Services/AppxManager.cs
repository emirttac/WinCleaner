using System.IO;
using System.Text;
using System.Text.Json;
using WinCleaner.Core.Abstractions;

namespace WinCleaner.Core.Services;

public sealed class InstalledAppInfo
{
    public required string PackageFullName { get; init; }
    public required string Name { get; init; }
    public required string PackageFamilyName { get; init; }
}

public sealed record PowerShellResult(bool Success, int ExitCode, string StdOut, string StdErr)
{
    public string ErrorDetail
    {
        get
        {
            var detail = string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdErr;
            detail = detail.Trim();
            if (string.IsNullOrWhiteSpace(detail))
                return $"PowerShell exited with code {ExitCode}";
            return detail.Length <= 500 ? detail : detail[..500] + "…";
        }
    }
}

public sealed class AppxManager
{
    private readonly IProcessRunner _processes;

    public AppxManager(IProcessRunner? processes = null)
    {
        _processes = processes ?? ProcessRunner.Shared;
    }

    public bool IsEdgeBrowserInstalled()
    {
        var x86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft\Edge\Application\msedge.exe");
        var x64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Microsoft\Edge\Application\msedge.exe");
        return File.Exists(x86) || File.Exists(x64);
    }

    public bool IsOneDriveInstalled()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\OneDrive\OneDrive.exe");
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Microsoft OneDrive\OneDrive.exe");
        var pf86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft OneDrive\OneDrive.exe");
        return File.Exists(local) || File.Exists(pf) || File.Exists(pf86);
    }

    public async Task<IReadOnlyList<InstalledAppInfo>> GetInstalledPackagesAsync(CancellationToken ct = default)
    {
        var script = @"
$ErrorActionPreference = 'SilentlyContinue'
$map = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
function Add-Pkg([string]$name, [string]$full, [string]$family) {
  if ([string]::IsNullOrWhiteSpace($name)) { return }
  if ($map.ContainsKey($name)) { return }
  $map[$name] = [pscustomobject]@{
    Name = $name
    PackageFullName = $full
    PackageFamilyName = $family
  }
}
foreach ($p in @(Get-AppxPackage)) {
  Add-Pkg $p.Name $p.PackageFullName $p.PackageFamilyName
}
try {
  foreach ($p in @(Get-AppxPackage -AllUsers -ErrorAction Stop)) {
    Add-Pkg $p.Name $p.PackageFullName $p.PackageFamilyName
  }
} catch {}
try {
  foreach ($p in @(Get-AppxProvisionedPackage -Online -ErrorAction Stop)) {
    Add-Pkg $p.DisplayName $p.PackageName ''
  }
} catch {}
if ($map.Count -eq 0) { '[]'; exit 0 }
ConvertTo-Json -InputObject @($map.Values) -Compress -Depth 3
";
        var result = await RunPowerShellAsync(script, ct, timeoutMs: 90_000).ConfigureAwait(false);
        var json = ExtractJson(result.StdOut);
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<InstalledAppInfo>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<InstalledAppInfo>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    list.Add(Parse(el));
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                list.Add(Parse(doc.RootElement));
            }
            return list;
        }
        catch
        {
            return Array.Empty<InstalledAppInfo>();
        }
    }

    public Task<PowerShellResult> RemovePackageAsync(string packageNamePattern, CancellationToken ct = default) =>
        RemovePackageAsync(new[] { packageNamePattern }, ct);

    public async Task<PowerShellResult> RemovePackageAsync(
        IReadOnlyList<string> packageNamePatterns,
        CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var patterns = packageNamePatterns
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (patterns.Count == 0)
            return new PowerShellResult(false, -1, "", "No package name specified");

        var quoted = string.Join(", ", patterns.Select(p => "'" + Escape(p) + "'"));
        var script = $@"
$ErrorActionPreference = 'Continue'
$patterns = @({quoted})

function Get-Matching([switch]$AllUsers) {{
  $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  foreach ($pattern in $patterns) {{
    $bags = @()
    if ($AllUsers) {{
      try {{ $bags = @(Get-AppxPackage -AllUsers -Name $pattern -ErrorAction SilentlyContinue) }} catch {{}}
    }} else {{
      $bags = @(Get-AppxPackage -Name $pattern -ErrorAction SilentlyContinue)
    }}
    foreach ($p in $bags) {{
      if ($p -and $p.PackageFullName) {{ [void]$set.Add([string]$p.PackageFullName) }}
    }}
  }}
  return @($set)
}}

Write-Output 'Removing packages for the current user'
foreach ($full in @(Get-Matching)) {{
  Write-Output ""remove $full""
  Remove-AppxPackage -Package $full -ErrorAction SilentlyContinue
}}

Write-Output 'Removing packages for all users'
foreach ($full in @(Get-Matching -AllUsers)) {{
  Write-Output ""remove-all $full""
  try {{
    Remove-AppxPackage -Package $full -AllUsers -ErrorAction Stop
  }} catch {{
    Remove-AppxPackage -Package $full -ErrorAction SilentlyContinue
  }}
}}

Write-Output 'Removing provisioned packages'
try {{
  $prov = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue)
  foreach ($p in $prov) {{
    $display = [string]$p.DisplayName
    foreach ($pattern in $patterns) {{
      if ($display -eq $pattern) {{
        Write-Output ""deprovision $display""
        Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName | Out-Null
        break
      }}
    }}
  }}
}} catch {{}}

$left = New-Object System.Collections.Generic.List[string]
foreach ($full in @(Get-Matching)) {{
  if (-not $left.Contains($full)) {{ $left.Add($full) }}
}}
if ($left.Count -gt 0) {{
  throw ""Package still installed after removal: $($left -join ', ')""
}}
Write-Output 'removed'
";
        return await RunPowerShellAsync(script, ct, timeoutMs: 180_000, progress).ConfigureAwait(false);
    }

    public async Task<PowerShellResult> UninstallOneDriveAsync(CancellationToken ct = default)
    {
        var script = @"
$ErrorActionPreference = 'Continue'

# Keep the user's sync folder if the uninstaller tries to delete it. Restored in finally.
$syncRoot = $env:OneDrive
$syncAcl = $null
$syncProtected = $false
if (-not [string]::IsNullOrWhiteSpace($syncRoot) -and (Test-Path -LiteralPath $syncRoot -PathType Container)) {
  try {
    $syncAcl = Get-Acl -LiteralPath $syncRoot
    & icacls.exe $syncRoot /deny '*S-1-5-32-544:(D,DC)' | Out-Null
    $syncProtected = $true
  } catch {}
}

trap {
  if ($syncProtected -and $syncAcl) {
    try { Set-Acl -LiteralPath $syncRoot -AclObject $syncAcl -ErrorAction SilentlyContinue } catch {}
  }
}

function Stop-NamedProcesses([string[]]$names) {
  foreach ($n in $names) {
    Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  }
}

# --- 1. Stop and uninstall (never hang forever on OneDriveSetup) ---
Stop-NamedProcesses @('OneDrive','OneDriveSetup','FileCoAuth')

# Prefer the registered uninstaller: modern OneDrive installs per-user under
# %LOCALAPPDATA%\Microsoft\OneDrive\<version>\ or per-machine under Program Files.
# The legacy System32/SysWOW64 stub no longer matches the installed version on Win11.
function Get-OneDriveUninstallCommands {
  $cmds = New-Object System.Collections.Generic.List[object]

  $uninstallKeys = @(
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe'
  )
  foreach ($k in $uninstallKeys) {
    $v = Get-ItemProperty -Path $k -ErrorAction SilentlyContinue
    if ($v -and $v.UninstallString) {
      $raw = [string]$v.UninstallString
      # Parse: ""C:\path\OneDriveSetup.exe"" /uninstall [/allusers]
      if ($raw -match '^\s*""([^""]+)""\s*(.*)$') {
        $exe = $Matches[1]; $argStr = $Matches[2].Trim()
      } else {
        $idx = $raw.IndexOf('.exe', [StringComparison]::OrdinalIgnoreCase)
        if ($idx -ge 0) { $exe = $raw.Substring(0, $idx + 4).Trim('""'); $argStr = $raw.Substring($idx + 4).Trim() }
        else { $exe = $raw; $argStr = '' }
      }
      if ($argStr -notmatch '/uninstall') { $argStr = ('/uninstall ' + $argStr).Trim() }
      if ($exe -and (Test-Path -LiteralPath $exe)) {
        $cmds.Add([pscustomobject]@{ Exe = $exe; Args = $argStr })
      }
    }
  }

  # Versioned setup binaries (per-user and per-machine layouts)
  $globRoots = @(
    @{ Root = ""$env:LOCALAPPDATA\Microsoft\OneDrive""; AllUsers = $false },
    @{ Root = ""${env:ProgramFiles}\Microsoft OneDrive""; AllUsers = $true },
    @{ Root = ""${env:ProgramFiles(x86)}\Microsoft OneDrive""; AllUsers = $true }
  )
  foreach ($g in $globRoots) {
    if (-not (Test-Path -LiteralPath $g.Root)) { continue }
    $setup = Get-ChildItem -LiteralPath $g.Root -Filter 'OneDriveSetup.exe' -Recurse -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($setup) {
      $argStr = if ($g.AllUsers) { '/uninstall /allusers' } else { '/uninstall' }
      $cmds.Add([pscustomobject]@{ Exe = $setup.FullName; Args = $argStr })
    }
  }

  # Legacy inbox stub as the last resort
  foreach ($stub in @(""$env:SYSTEMROOT\SysWOW64\OneDriveSetup.exe"", ""$env:SYSTEMROOT\System32\OneDriveSetup.exe"")) {
    if (Test-Path -LiteralPath $stub) {
      $cmds.Add([pscustomobject]@{ Exe = $stub; Args = '/uninstall' })
    }
  }

  return $cmds
}

function Test-OneDriveInstalled {
  $probes = @(
    ""$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"",
    ""${env:ProgramFiles}\Microsoft OneDrive\OneDrive.exe"",
    ""${env:ProgramFiles(x86)}\Microsoft OneDrive\OneDrive.exe""
  )
  foreach ($p in $probes) { if (Test-Path -LiteralPath $p) { return $true } }
  return $false
}

foreach ($cmd in @(Get-OneDriveUninstallCommands)) {
  if (-not (Test-OneDriveInstalled)) { break }
  $p = Start-Process -FilePath $cmd.Exe -ArgumentList $cmd.Args -PassThru -WindowStyle Hidden
  if ($p -and -not $p.WaitForExit(120000)) {
    try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
  Stop-NamedProcesses @('OneDrive','OneDriveSetup','FileCoAuth')
}

Stop-NamedProcesses @('OneDrive','OneDriveSetup','FileCoAuth')

function Remove-PathSafe([string]$p) {
  if (-not (Test-Path -LiteralPath $p)) { return }
  try {
    if (Test-Path -LiteralPath $p -PathType Leaf) {
      Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
    } else {
      Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue } catch {}
      }
    }
  } catch {}
}

# --- 2. Remaining setup / install leftovers (do not delete user Documents\OneDrive sync root) ---
$setupPaths = @(
  ""${env:ProgramFiles}\Microsoft OneDrive"",
  ""${env:ProgramFiles(x86)}\Microsoft OneDrive"",
  ""$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"",
  ""$env:LOCALAPPDATA\Microsoft\OneDrive\Update\OneDriveSetup.exe""
)
foreach ($p in $setupPaths) { Remove-PathSafe $p }

# --- 3. AppData remnants ---
$appDataPaths = @(
  ""$env:LOCALAPPDATA\Microsoft\OneDrive"",
  ""$env:APPDATA\Microsoft\OneDrive"",
  ""$env:LOCALAPPDATA\OneDrive""
)
foreach ($p in $appDataPaths) { Remove-PathSafe $p }

# --- 4. Registry traces ---
Remove-Item -Path 'HKCU:\Software\Microsoft\OneDrive' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Classes\Wow6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'OneDriveSetup' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDrive' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'OneDrive' -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileCoAuth' -Recurse -Force -ErrorAction SilentlyContinue
Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like '*OneDrive*' } | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue

$overlayRoot = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers'
if (Test-Path $overlayRoot) {
  Get-ChildItem $overlayRoot -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.PSChildName -match 'OneDrive') {
      Remove-Item -LiteralPath $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

# --- 5. Restore sync-folder access, then verify the client is gone ---
if ($syncProtected -and $syncAcl) {
  try { Set-Acl -LiteralPath $syncRoot -AclObject $syncAcl -ErrorAction SilentlyContinue } catch {}
}
Stop-NamedProcesses @('OneDrive','OneDriveSetup','FileCoAuth')
$leftover = New-Object System.Collections.Generic.List[string]
$exeCandidates = @(
  ""$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"",
  ""${env:ProgramFiles}\Microsoft OneDrive\OneDrive.exe"",
  ""${env:ProgramFiles(x86)}\Microsoft OneDrive\OneDrive.exe""
)
foreach ($p in $exeCandidates) {
  if (Test-Path -LiteralPath $p) { $leftover.Add($p) }
}
if (Get-Process -Name OneDrive -ErrorAction SilentlyContinue) {
  $leftover.Add('process:OneDrive')
}
if ($leftover.Count -gt 0) {
  throw ""OneDrive leftover after uninstall: $($leftover -join '; ')""
}
";
        return await RunPowerShellAsync(script, ct, timeoutMs: 180_000).ConfigureAwait(false);
    }

    public async Task<PowerShellResult> UninstallEdgeAsync(CancellationToken ct = default)
    {
        // Official Chromium Edge uninstall requires --msedge [--channel=...]. Without it,
        // setup.exe exits without removing the browser. Prefer the registered UninstallString.
        // Kill only Edge browser/updater trees — never WebView2.
        var script = @"
$ErrorActionPreference = 'Continue'

function Test-IsWebViewPath([string]$p) {
  if ([string]::IsNullOrWhiteSpace($p)) { return $true }
  return $p -like '*EdgeWebView*'
}

function Stop-EdgeBrowserTree {
  # Path-based kill: anything under Microsoft\Edge\ except EdgeWebView.
  try {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
      $_.ExecutablePath -and
      $_.ExecutablePath -like '*\Microsoft\Edge\*' -and
      $_.ExecutablePath -notlike '*EdgeWebView*' -and
      $_.Name -ne 'msedgewebview2.exe'
    } | ForEach-Object {
      try { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } catch {}
    }
  } catch {}

  foreach ($n in @(
    'msedge','msedge_proxy','identity_helper','elevation_service','notification_helper',
    'browser_broker','MicrosoftEdgeUpdate','MicrosoftEdgeUpdateCore','MicrosoftEdgeUpdateBroker'
  )) {
    & taskkill.exe /F /IM ($n + '.exe') /T 2>$null | Out-Null
    Get-Process -Name $n -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  }
}

function Test-EdgeBrowserInstalled {
  $exeProbes = @(
    ""${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"",
    ""${env:ProgramFiles}\Microsoft\Edge\Application\msedge.exe""
  )
  foreach ($p in $exeProbes) { if (Test-Path -LiteralPath $p) { return $true } }
  return $false
}

function Parse-UninstallCommand([string]$raw) {
  if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
  $exe = $null
  $argStr = ''
  if ($raw -match '^\s*""([^""]+)""\s*(.*)$') {
    $exe = $Matches[1]
    $argStr = $Matches[2].Trim()
  } else {
    $idx = $raw.IndexOf('.exe', [StringComparison]::OrdinalIgnoreCase)
    if ($idx -ge 0) {
      $exe = $raw.Substring(0, $idx + 4).Trim('""')
      $argStr = $raw.Substring($idx + 4).Trim()
    } else {
      $exe = $raw.Trim('""')
    }
  }
  if (-not $exe -or -not (Test-Path -LiteralPath $exe)) { return $null }
  if (Test-IsWebViewPath $exe) { return $null }
  return [pscustomobject]@{ Exe = $exe; Args = $argStr }
}

function Ensure-EdgeUninstallArgs([string]$argStr) {
  $parts = New-Object System.Collections.Generic.List[string]
  if (-not [string]::IsNullOrWhiteSpace($argStr)) {
    foreach ($tok in ($argStr -split '\s+')) {
      if (-not [string]::IsNullOrWhiteSpace($tok)) { $parts.Add($tok) }
    }
  }
  if (-not ($parts | Where-Object { $_ -ieq '--uninstall' })) { $parts.Insert(0, '--uninstall') }
  if (-not ($parts | Where-Object { $_ -ieq '--msedge' })) { $parts.Add('--msedge') }
  if (-not ($parts | Where-Object { $_ -like '--channel=*' })) { $parts.Add('--channel=stable') }
  if (-not ($parts | Where-Object { $_ -ieq '--system-level' })) { $parts.Add('--system-level') }
  if (-not ($parts | Where-Object { $_ -ieq '--verbose-logging' })) { $parts.Add('--verbose-logging') }
  if (-not ($parts | Where-Object { $_ -ieq '--force-uninstall' })) { $parts.Add('--force-uninstall') }
  if (-not ($parts | Where-Object { $_ -ieq '--delete-profile' })) { $parts.Add('--delete-profile') }
  return ($parts -join ' ')
}

function Get-EdgeSetupCommands {
  $cmds = New-Object System.Collections.Generic.List[object]
  $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

  $uninstallKeys = @(
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge'
  )
  foreach ($k in $uninstallKeys) {
    $v = Get-ItemProperty -Path $k -ErrorAction SilentlyContinue
    if (-not $v) { continue }
    foreach ($prop in @('QuietUninstallString','UninstallString')) {
      $parsed = Parse-UninstallCommand ([string]$v.$prop)
      if (-not $parsed) { continue }
      $args = Ensure-EdgeUninstallArgs $parsed.Args
      $key = ($parsed.Exe + '|' + $args)
      if ($seen.Add($key)) {
        $cmds.Add([pscustomobject]@{ Exe = $parsed.Exe; Args = $args })
      }
    }
  }

  $roots = @(
    ""${env:ProgramFiles(x86)}\Microsoft\Edge\Application"",
    ""${env:ProgramFiles}\Microsoft\Edge\Application""
  )
  foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Filter 'setup.exe' -Recurse -ErrorAction SilentlyContinue |
      Where-Object {
        -not (Test-IsWebViewPath $_.FullName) -and
        $_.DirectoryName -like '*\Installer'
      } |
      Sort-Object LastWriteTime -Descending |
      Select-Object -First 2 |
      ForEach-Object {
        $args = Ensure-EdgeUninstallArgs ''
        $key = ($_.FullName + '|' + $args)
        if ($seen.Add($key)) {
          $cmds.Add([pscustomobject]@{ Exe = $_.FullName; Args = $args })
        }
      }
  }
  return $cmds
}

function Invoke-EdgeCommand($cmd, [int]$timeoutMs) {
  if (Test-IsWebViewPath $cmd.Exe) { return }
  $work = Split-Path -Parent $cmd.Exe
  # Single argument string keeps setup.exe flag parsing stable.
  $p = Start-Process -FilePath $cmd.Exe -ArgumentList $cmd.Args -WorkingDirectory $work -PassThru -WindowStyle Hidden
  if ($p -and -not $p.WaitForExit($timeoutMs)) {
    try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
}

function Force-RemoveEdgeTree([string]$folder) {
  if (Test-IsWebViewPath $folder) { return }
  if (-not (Test-Path -LiteralPath $folder)) { return }
  try {
    & takeown.exe /F $folder /R /D Y 2>$null | Out-Null
    & icacls.exe $folder /grant ""*S-1-5-32-544:(OI)(CI)F"" /T /C /Q 2>$null | Out-Null
  } catch {}
  try { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue } catch {}
  if (Test-Path -LiteralPath $folder) {
    & cmd.exe /c ""rmdir /s /q """"$folder"""""" 2>$null | Out-Null
  }
}

# 1) Kill Edge browser tree hard (not WebView2)
Stop-EdgeBrowserTree

foreach ($svc in @('edgeupdate','edgeupdatem','MicrosoftEdgeElevationService')) {
  try { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue } catch {}
  try { Set-Service -Name $svc -StartupType Disabled -ErrorAction SilentlyContinue } catch {}
}

# 2) Official setup uninstall (registry first), twice if needed
$cmds = @(Get-EdgeSetupCommands)
for ($pass = 0; $pass -lt 2; $pass++) {
  Stop-EdgeBrowserTree
  foreach ($cmd in $cmds) {
    if (-not (Test-EdgeBrowserInstalled)) { break }
    Invoke-EdgeCommand $cmd 240000
    Stop-EdgeBrowserTree
  }
  if (-not (Test-EdgeBrowserInstalled)) { break }
  Start-Sleep -Seconds 2
}

# 3) AppX stub (browser only)
try {
  Get-AppxPackage -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'Microsoft.MicrosoftEdge.Stable' } |
    ForEach-Object {
      Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue
    }
} catch {}

# 4) Updater uninstall + delete services/tasks
Stop-EdgeBrowserTree
foreach ($exe in @(
  ""${env:ProgramFiles(x86)}\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe"",
  ""${env:ProgramFiles}\Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe""
)) {
  if (Test-IsWebViewPath $exe) { continue }
  if (Test-Path -LiteralPath $exe) {
    Invoke-EdgeCommand ([pscustomobject]@{ Exe = $exe; Args = '/uninstall' }) 120000
  }
}

foreach ($svc in @('edgeupdate','edgeupdatem','MicrosoftEdgeElevationService')) {
  try { Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue } catch {}
  try { & sc.exe delete $svc 2>$null | Out-Null } catch {}
}

Get-ScheduledTask -ErrorAction SilentlyContinue |
  Where-Object { $_.TaskName -like 'MicrosoftEdgeUpdate*' } |
  ForEach-Object { Unregister-ScheduledTask -TaskName $_.TaskName -Confirm:$false -ErrorAction SilentlyContinue }

# 5) Force-delete leftover trees (Edge + EdgeUpdate only)
Stop-EdgeBrowserTree
foreach ($folder in @(
  ""${env:ProgramFiles(x86)}\Microsoft\Edge"",
  ""${env:ProgramFiles}\Microsoft\Edge"",
  ""${env:ProgramFiles(x86)}\Microsoft\EdgeUpdate"",
  ""${env:ProgramFiles}\Microsoft\EdgeUpdate""
)) {
  Force-RemoveEdgeTree $folder
}

# 6) Clear uninstall ARP entries so leftovers are not treated as installed
foreach ($k in @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
  'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge',
  'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Edge'
)) {
  Remove-Item -Path $k -Recurse -Force -ErrorAction SilentlyContinue
}

try {
  if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate')) {
    New-Item -Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Force | Out-Null
  }
  New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\EdgeUpdate' -Name 'DoNotUpdateToEdgeWithChromium' -PropertyType DWord -Value 1 -Force | Out-Null
} catch {}

if (Test-EdgeBrowserInstalled) {
  Write-Output 'EDGE_STILL_INSTALLED'
  exit 2
}

Write-Output 'removed'
";
        return await RunPowerShellAsync(script, ct, timeoutMs: 420_000).ConfigureAwait(false);
    }

    private static InstalledAppInfo Parse(JsonElement el) => new()
    {
        Name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
        PackageFullName = el.TryGetProperty("PackageFullName", out var p) ? p.GetString() ?? "" : "",
        PackageFamilyName = el.TryGetProperty("PackageFamilyName", out var f) ? f.GetString() ?? "" : ""
    };

    private static string? ExtractJson(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;
        var raw = stdout.Trim();
        const string marker = "#< CLIXML";
        var searchFrom = 0;
        while (true)
        {
            var startXml = raw.IndexOf(marker, searchFrom, StringComparison.Ordinal);
            if (startXml < 0)
                break;
            var endXml = raw.IndexOf("</Objs>", startXml, StringComparison.Ordinal);
            if (endXml < 0)
            {
                raw = raw[..startXml].Trim();
                break;
            }
            raw = (raw[..startXml] + raw[(endXml + 7)..]).Trim();
            searchFrom = startXml;
        }

        var arrayStart = raw.IndexOf('[');
        var objectStart = raw.IndexOf('{');
        var start = -1;
        if (arrayStart >= 0 && (objectStart < 0 || arrayStart < objectStart))
            start = arrayStart;
        else if (objectStart >= 0)
            start = objectStart;
        return start < 0 ? null : raw[start..];
    }

    private static string Escape(string s) => s.Replace("'", "''");

    private async Task<PowerShellResult> RunPowerShellAsync(
        string script,
        CancellationToken ct,
        int timeoutMs = 120_000,
        IProgress<string>? progress = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "WinCleaner");
        Directory.CreateDirectory(dir);
        var scriptPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".ps1");
        var body = "$ProgressPreference = 'SilentlyContinue'\n$WarningPreference = 'SilentlyContinue'\n" + script;
        await File.WriteAllTextAsync(scriptPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct).ConfigureAwait(false);
        try
        {
            var exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(exe))
                exe = "powershell.exe";
            var arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
            var result = await _processes.RunAsync(
                new ProcessRequest(exe, arguments, TimeSpan.FromMilliseconds(timeoutMs)) { LineProgress = progress },
                ct).ConfigureAwait(false);

            var stdout = result.Stdout.Trim();
            var stderr = result.Stderr.Trim();
            if (result.Cancelled)
                return new PowerShellResult(false, -1, stdout, "msg.processCancelled");
            if (result.TimedOut)
                return new PowerShellResult(false, -1, stdout, "msg.processTimedOut");
            if (result.ExitCode is null)
                return new PowerShellResult(false, -1, stdout, "msg.processNoExit");

            var success = result.ExitCode == 0;
            if (success && !string.IsNullOrWhiteSpace(stderr) &&
                (stderr.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                 || stderr.Contains("Error", StringComparison.OrdinalIgnoreCase)
                 || stderr.Contains("fullyqualifiederrorid", StringComparison.OrdinalIgnoreCase)))
                success = false;

            return new PowerShellResult(success, result.ExitCode.Value, stdout, stderr);
        }
        finally
        {
            try { File.Delete(scriptPath); }
            catch { /* the script is only a launch vehicle */ }
        }
    }
}

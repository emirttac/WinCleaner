using System.Diagnostics;
using System.Text;
using System.Text.Json;

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

    public async Task<PowerShellResult> RemovePackageAsync(IReadOnlyList<string> packageNamePatterns, CancellationToken ct = default)
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
$ErrorActionPreference = 'SilentlyContinue'
$patterns = @({quoted})
$fullNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

function Add-MatchingPackages([string]$name) {{
  $bags = @()
  try {{ $bags += @(Get-AppxPackage -AllUsers -Name $name -ErrorAction SilentlyContinue) }} catch {{}}
  $bags += @(Get-AppxPackage -Name $name -ErrorAction SilentlyContinue)
  foreach ($p in $bags) {{
    if ($p -and $p.PackageFullName) {{ [void]$fullNames.Add([string]$p.PackageFullName) }}
  }}
}}

foreach ($pattern in $patterns) {{
  Add-MatchingPackages $pattern
}}

foreach ($full in @($fullNames)) {{
  try {{
    Remove-AppxPackage -Package $full -AllUsers -ErrorAction Stop
  }} catch {{
    try {{
      Remove-AppxPackage -Package $full -ErrorAction Stop
    }} catch {{}}
  }}
}}

try {{
  $prov = @(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue)
  foreach ($p in $prov) {{
    $display = [string]$p.DisplayName
    $hit = $false
    foreach ($pattern in $patterns) {{
      if ($display -eq $pattern) {{ $hit = $true; break }}
    }}
    if ($hit) {{
      Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName | Out-Null
    }}
  }}
}} catch {{}}

$remaining = New-Object System.Collections.Generic.List[string]
foreach ($pattern in $patterns) {{
  $bags = @()
  try {{ $bags += @(Get-AppxPackage -AllUsers -Name $pattern -ErrorAction SilentlyContinue) }} catch {{}}
  $bags += @(Get-AppxPackage -Name $pattern -ErrorAction SilentlyContinue)
  foreach ($p in $bags) {{
    if ($p -and $p.Name -and -not $remaining.Contains([string]$p.Name)) {{
      $remaining.Add([string]$p.Name)
    }}
  }}
}}
if ($remaining.Count -gt 0) {{
  throw ""Package still installed after removal: $($remaining -join ', ')""
}}
";
        return await RunPowerShellAsync(script, ct, timeoutMs: 120_000).ConfigureAwait(false);
    }

    public async Task<PowerShellResult> UninstallOneDriveAsync(CancellationToken ct = default)
    {
        var script = @"
$ErrorActionPreference = 'Continue'

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

# --- 5. Verify the client is gone (do not treat System32 OneDriveSetup.exe as leftover) ---
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

    private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken ct, int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var bytes = Encoding.Unicode.GetBytes(
            "$ProgressPreference = 'SilentlyContinue'\n$WarningPreference = 'SilentlyContinue'\n" + script);
        var encoded = Convert.ToBase64String(bytes);
        psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        var linked = timeoutCts.Token;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(linked).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            return new PowerShellResult(false, -1, "", "Timed out");
        }

        string stdout;
        string stderr;
        try
        {
            stdout = await stdoutTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            stderr = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            stdout = "";
            stderr = proc.HasExited ? "" : "Timed out reading PowerShell output";
        }

        var success = proc.ExitCode == 0 && string.IsNullOrWhiteSpace(stderr);
        // Some cmdlets write progress to stderr without failing — treat non-zero exit as definitive failure;
        // non-empty stderr with exit 0 is also treated as failure to surface silent cmdlet errors.
        if (proc.ExitCode != 0)
            success = false;
        else if (!string.IsNullOrWhiteSpace(stderr) &&
                 (stderr.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                  || stderr.Contains("Error", StringComparison.OrdinalIgnoreCase)
                  || stderr.Contains("fullyqualifiederrorid", StringComparison.OrdinalIgnoreCase)))
            success = false;
        else if (proc.ExitCode == 0)
            success = true;

        return new PowerShellResult(success, proc.ExitCode, stdout.Trim(), stderr.Trim());
    }
}

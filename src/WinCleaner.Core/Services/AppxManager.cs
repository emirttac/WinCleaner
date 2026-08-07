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
        var script = "Get-AppxPackage | Select-Object Name, PackageFullName, PackageFamilyName | ConvertTo-Json -Compress";
        var result = await RunPowerShellAsync(script, ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StdOut))
            return Array.Empty<InstalledAppInfo>();

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
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

    public async Task<PowerShellResult> RemovePackageAsync(string packageNamePattern, CancellationToken ct = default)
    {
        var escaped = Escape(packageNamePattern);
        var script = $@"
$ErrorActionPreference = 'Stop'
$pattern = '{escaped}*'
$pkgs = @(Get-AppxPackage -Name $pattern -ErrorAction SilentlyContinue)
foreach ($p in $pkgs) {{
  Remove-AppxPackage -Package $p.PackageFullName
}}
$prov = @(Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like $pattern }})
foreach ($p in $prov) {{
  Remove-AppxProvisionedPackage -Online -PackageName $p.PackageName | Out-Null
}}
$remaining = @(Get-AppxPackage -Name $pattern -ErrorAction SilentlyContinue)
if ($remaining.Count -gt 0) {{
  throw ""Package still installed after removal: $($remaining[0].Name)""
}}
";
        return await RunPowerShellAsync(script, ct).ConfigureAwait(false);
    }

    public async Task<PowerShellResult> UninstallOneDriveAsync(CancellationToken ct = default)
    {
        var script = @"
$ErrorActionPreference = 'Stop'

# --- 1. Stop and uninstall ---
$onedrive = ""$env:SYSTEMROOT\SysWOW64\OneDriveSetup.exe""
if (-not (Test-Path $onedrive)) { $onedrive = ""$env:SYSTEMROOT\System32\OneDriveSetup.exe"" }
Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue
if (Test-Path $onedrive) {
  Start-Process -FilePath $onedrive -ArgumentList '/uninstall' -Wait -NoNewWindow
}

# --- 2. Remaining setup / install folders ---
$setupPaths = @(
  ""$env:SYSTEMROOT\SysWOW64\OneDriveSetup.exe"",
  ""$env:SYSTEMROOT\System32\OneDriveSetup.exe"",
  ""${env:ProgramFiles}\Microsoft OneDrive"",
  ""${env:ProgramFiles(x86)}\Microsoft OneDrive"",
  ""$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe"",
  ""$env:LOCALAPPDATA\Microsoft\OneDrive\Update\OneDriveSetup.exe""
)
foreach ($p in $setupPaths) {
  if (Test-Path $p) {
    try {
      if (Test-Path $p -PathType Leaf) { Remove-Item -LiteralPath $p -Force -ErrorAction Stop }
      else { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop }
    } catch {
      # Locked files after uninstall are non-fatal; continue cleanup
    }
  }
}

# --- 3. AppData remnants (app data only — do not delete user sync root contents) ---
$appDataPaths = @(
  ""$env:LOCALAPPDATA\Microsoft\OneDrive"",
  ""$env:APPDATA\Microsoft\OneDrive"",
  ""$env:LOCALAPPDATA\OneDrive""
)
foreach ($p in $appDataPaths) {
  if (Test-Path $p) {
    try { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction Stop } catch { }
  }
}

# --- 4. Registry traces ---
$ErrorActionPreference = 'Continue'
Remove-Item -Path 'HKCU:\Software\Microsoft\OneDrive' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Classes\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Classes\Wow6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}' -Recurse -Force -ErrorAction SilentlyContinue

# FileCoAuth / Explorer integration
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer' -Name 'OneDriveSetup' -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileCoAuth' -Recurse -Force -ErrorAction SilentlyContinue

# Shell icon overlay identifiers that reference OneDrive
$overlayRoot = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers'
if (Test-Path $overlayRoot) {
  Get-ChildItem $overlayRoot -ErrorAction SilentlyContinue | ForEach-Object {
    if ($_.PSChildName -match 'OneDrive') {
      Remove-Item -LiteralPath $_.PSPath -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

$ErrorActionPreference = 'Stop'
";
        return await RunPowerShellAsync(script, ct).ConfigureAwait(false);
    }

    private static InstalledAppInfo Parse(JsonElement el) => new()
    {
        Name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
        PackageFullName = el.TryGetProperty("PackageFullName", out var p) ? p.GetString() ?? "" : "",
        PackageFamilyName = el.TryGetProperty("PackageFamilyName", out var f) ? f.GetString() ?? "" : ""
    };

    private static string Escape(string s) => s.Replace("'", "''");

    private static async Task<PowerShellResult> RunPowerShellAsync(string script, CancellationToken ct)
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

        var bytes = Encoding.Unicode.GetBytes(script);
        var encoded = Convert.ToBase64String(bytes);
        psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

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

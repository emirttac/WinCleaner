using System.ServiceProcess;
using Microsoft.Win32;
using WinCleaner.Core.Models;

namespace WinCleaner.Core.Services;

public sealed class WindowsServiceInfo
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public ServiceControllerStatus Status { get; init; }
    public ServiceStartMode StartType { get; init; }
}

public sealed class ServiceManager
{
    public IReadOnlyList<WindowsServiceInfo> GetAllServices()
    {
        var result = new List<WindowsServiceInfo>();
        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                result.Add(new WindowsServiceInfo
                {
                    ServiceName = sc.ServiceName,
                    DisplayName = sc.DisplayName,
                    Status = sc.Status,
                    StartType = sc.StartType,
                    Description = GetDescription(sc.ServiceName)
                });
            }
            catch
            {
                // skip inaccessible services
            }
            finally
            {
                sc.Dispose();
            }
        }
        return result.OrderBy(s => s.DisplayName).ToList();
    }

    public WindowsServiceInfo? GetService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return new WindowsServiceInfo
            {
                ServiceName = sc.ServiceName,
                DisplayName = sc.DisplayName,
                Status = sc.Status,
                StartType = sc.StartType,
                Description = GetDescription(sc.ServiceName)
            };
        }
        catch
        {
            return null;
        }
    }

    public string ResolveExistingName(string primary, IEnumerable<string>? aliases = null)
    {
        if (GetService(primary) is not null)
            return primary;
        if (aliases is not null)
        {
            foreach (var alias in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                var trimmed = alias.Trim();
                if (GetService(trimmed) is not null)
                    return trimmed;
            }
        }
        return primary;
    }

    public void SetStartType(string serviceName, ServiceStartModeTarget target)
    {
        var mode = target switch
        {
            ServiceStartModeTarget.Automatic => "auto",
            ServiceStartModeTarget.Manual => "demand",
            ServiceStartModeTarget.Disabled => "disabled",
            _ => "demand"
        };

        RunSc($"config \"{serviceName}\" start= {mode}");
    }

    public void Start(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        if (sc.Status == ServiceControllerStatus.Running) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public void Stop(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        if (sc.Status == ServiceControllerStatus.Stopped) return;
        if (!sc.CanStop) throw new InvalidOperationException($"Service {serviceName} cannot be stopped.");
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    public ServiceStartMode GetStartType(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        return sc.StartType;
    }

    private static string GetDescription(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("Description")?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static void RunSc(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sc.exe");
        proc.WaitForExit(15000);
        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            var output = proc.StandardOutput.ReadToEnd();
            throw new InvalidOperationException($"sc.exe failed ({proc.ExitCode}): {err} {output}".Trim());
        }
    }
}

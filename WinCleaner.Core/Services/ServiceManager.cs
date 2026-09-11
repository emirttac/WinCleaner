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
            return ToInfo(sc);
        }
        catch
        {
            // Per-user services are registered as TemplateName_hex (e.g. OneSyncSvc_1a2b3c).
        }

        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                if (ServiceNameMatcher.IsInstanceOf(sc.ServiceName, serviceName))
                    return ToInfo(sc);
            }
            catch
            {
                // skip inaccessible instance
            }
            finally
            {
                sc.Dispose();
            }
        }

        return null;
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

        Exception? last = null;
        var ok = 0;
        foreach (var name in ExpandTargets(serviceName))
        {
            try
            {
                RunSc($"config \"{name}\" start= {mode}");
                ok++;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (ok == 0)
            throw last ?? new InvalidOperationException($"Service {serviceName} not found.");
    }

    public void Start(string serviceName)
    {
        Exception? last = null;
        var attempted = false;
        foreach (var name in ExpandTargets(serviceName))
        {
            attempted = true;
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Running) continue;
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (!attempted && last is not null)
            throw last;
    }

    public void Stop(string serviceName)
    {
        Exception? last = null;
        var stopped = false;
        foreach (var name in ExpandTargets(serviceName))
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    stopped = true;
                    continue;
                }
                if (!sc.CanStop) continue;
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                stopped = true;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (!stopped && last is not null)
            throw last;
    }

    public ServiceStartMode GetStartType(string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        return sc.StartType;
    }

    public IReadOnlyList<string> ExpandTargets(string serviceName)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { serviceName };
        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                if (sc.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)
                    || ServiceNameMatcher.IsInstanceOf(sc.ServiceName, serviceName))
                    names.Add(sc.ServiceName);
            }
            finally
            {
                sc.Dispose();
            }
        }

        return names.ToList();
    }

    private static WindowsServiceInfo ToInfo(ServiceController sc) =>
        new()
        {
            ServiceName = sc.ServiceName,
            DisplayName = sc.DisplayName,
            Status = sc.Status,
            StartType = sc.StartType,
            Description = GetDescription(sc.ServiceName)
        };

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

/// <summary>
/// Matches catalog SCM names to live services, including per-user instances (Name_hex).
/// </summary>
public static class ServiceNameMatcher
{
    public static bool IsInstanceOf(string liveName, string catalogName)
    {
        if (string.IsNullOrWhiteSpace(liveName) || string.IsNullOrWhiteSpace(catalogName))
            return false;
        if (liveName.Equals(catalogName, StringComparison.OrdinalIgnoreCase))
            return true;
        return liveName.StartsWith(catalogName + "_", StringComparison.OrdinalIgnoreCase);
    }

    public static TValue? MatchLive<TValue>(
        IReadOnlyDictionary<string, TValue> live,
        IEnumerable<string> catalogNames)
        where TValue : class
    {
        var names = new List<string>();
        foreach (var name in catalogNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            names.Add(name.Trim());
        }

        foreach (var name in names)
        {
            if (live.TryGetValue(name, out var exact))
                return exact;
        }

        foreach (var name in names)
        {
            foreach (var kv in live)
            {
                if (IsInstanceOf(kv.Key, name))
                    return kv.Value;
            }
        }

        return null;
    }
}

using System.Management;
using System.Net;
using System.Net.NetworkInformation;

namespace WinCleaner.Core.Services;

public enum DnsPresetKind
{
    Dhcp,
    Cloudflare,
    Google,
    Quad9
}

public sealed record DnsPreset(
    DnsPresetKind Kind,
    string Id,
    string DisplayNameKey,
    string? Primary,
    string? Secondary)
{
    public bool IsDhcp => Kind == DnsPresetKind.Dhcp;

    public string[]? Servers =>
        IsDhcp ? null
        : Secondary is null ? [Primary!]
        : [Primary!, Secondary];
}

public sealed class DnsApplyResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int AdaptersUpdated { get; init; }
    public bool FlushDnsSucceeded { get; init; }
}

/// <summary>
/// Changes DNS servers on IP-enabled adapters via WMI Win32_NetworkAdapterConfiguration,
/// then flushes the DNS resolver cache.
/// </summary>
public sealed class DnsChangerService
{
    public static IReadOnlyList<DnsPreset> Presets { get; } =
    [
        new(DnsPresetKind.Dhcp, "dhcp", "net.dns.dhcp", null, null),
        new(DnsPresetKind.Cloudflare, "cloudflare", "net.dns.cloudflare", "1.1.1.1", "1.0.0.1"),
        new(DnsPresetKind.Google, "google", "net.dns.google", "8.8.8.8", "8.8.4.4"),
        new(DnsPresetKind.Quad9, "quad9", "net.dns.quad9", "9.9.9.9", "149.112.112.112")
    ];

    public DnsPresetKind DetectCurrentPreset()
    {
        try
        {
            var servers = GetActiveDnsServers();
            if (servers.Count == 0)
                return DnsPresetKind.Dhcp;

            foreach (var preset in Presets.Where(p => !p.IsDhcp))
            {
                if (MatchesPreset(servers, preset))
                    return preset.Kind;
            }
        }
        catch
        {
            // Fall through to DHCP
        }

        return DnsPresetKind.Dhcp;
    }

    public DnsApplyResult ApplyPreset(DnsPresetKind kind)
    {
        var preset = Presets.FirstOrDefault(p => p.Kind == kind)
                     ?? Presets[0];

        try
        {
            if (!PrivilegeHelper.IsAdministrator())
            {
                return new DnsApplyResult
                {
                    Success = false,
                    Message = "Administrator privileges are required to change DNS."
                };
            }

            var updated = SetDnsOnIpEnabledAdapters(preset.Servers);
            var flush = FlushDnsCache();

            return new DnsApplyResult
            {
                Success = updated > 0 || preset.IsDhcp,
                AdaptersUpdated = updated,
                FlushDnsSucceeded = flush.Success,
                Message = updated == 0 && !preset.IsDhcp
                    ? "No IP-enabled adapters were updated."
                    : $"DNS updated on {updated} adapter(s)."
                      + (flush.Success ? " DNS cache flushed." : $" Flush DNS: {flush.ErrorMessage}")
            };
        }
        catch (Exception ex)
        {
            return new DnsApplyResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public (bool Success, string? ErrorMessage) FlushDnsCache()
    {
        var run = ElevatedCommandRunner.Run("ipconfig", "/flushdns");
        return run.Success
            ? (true, null)
            : (false, run.ErrorMessage ?? "ipconfig /flushdns failed");
    }

    private static int SetDnsOnIpEnabledAdapters(string[]? servers)
    {
        var updated = 0;
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");

        foreach (ManagementObject adapter in searcher.Get())
        {
            using (adapter)
            {
                try
                {
                    var inParams = adapter.GetMethodParameters("SetDNSServerSearchOrder");
                    // null => obtain DNS from DHCP; otherwise explicit server list
                    inParams["DNSServerSearchOrder"] = servers is null || servers.Length == 0
                        ? null
                        : servers;

                    var outParams = adapter.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
                    var code = outParams is null ? -1 : Convert.ToInt32(outParams["ReturnValue"]);
                    // 0 = Success, 1 = Success reboot required
                    if (code is 0 or 1)
                        updated++;
                }
                catch
                {
                    // Skip adapters that reject the call
                }
            }
        }

        return updated;
    }

    private static List<string> GetActiveDnsServers()
    {
        var list = new List<string>();

        // Prefer NetworkInterface for a quick read of current DNS
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var props = nic.GetIPProperties();
            foreach (var dns in props.DnsAddresses)
            {
                if (dns.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    continue;
                var s = dns.ToString();
                if (!list.Contains(s, StringComparer.OrdinalIgnoreCase))
                    list.Add(s);
            }
        }

        if (list.Count > 0)
            return list;

        // Fallback: WMI DNSServerSearchOrder
        using var searcher = new ManagementObjectSearcher(
            "SELECT DNSServerSearchOrder FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = True");
        foreach (ManagementObject adapter in searcher.Get())
        {
            using (adapter)
            {
                if (adapter["DNSServerSearchOrder"] is string[] arr)
                {
                    foreach (var s in arr)
                    {
                        if (!string.IsNullOrWhiteSpace(s) && IPAddress.TryParse(s, out _))
                            list.Add(s);
                    }
                }
            }
        }

        return list;
    }

    private static bool MatchesPreset(IReadOnlyList<string> current, DnsPreset preset)
    {
        if (preset.Primary is null) return false;
        if (!current.Any(s => s.Equals(preset.Primary, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (preset.Secondary is not null &&
            !current.Any(s => s.Equals(preset.Secondary, StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }
}

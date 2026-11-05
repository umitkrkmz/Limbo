using System.Net.NetworkInformation;

namespace Limbo.Console.Interactive;

/// <summary>
/// Reads ambient system/network state used by the interactive dashboard:
/// administrator privileges and the DNS servers configured on the machine's
/// primary (gateway-having, up) network interface.
/// </summary>
internal static class SystemStatus
{
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Finds the network interface most likely to be "the" active connection
    /// (up, with a default gateway) and returns its name plus the DNS
    /// servers Windows has configured for it. Returns null if none found.
    /// </summary>
    /// <summary>
    /// Splits DNS servers by IP version. Windows can report a server twice, and the
    /// "%13" scope suffix of a link-local router address is noise for display.
    /// </summary>
    public static (IReadOnlyList<string> IPv4, IReadOnlyList<string> IPv6) SplitByFamily(IEnumerable<string> servers)
    {
        var distinct = servers.Select(s => s.Split('%')[0]).Distinct().ToList();
        return ([.. distinct.Where(s => !s.Contains(':'))], [.. distinct.Where(s => s.Contains(':'))]);
    }

    public static (string Name, IReadOnlyList<string> DnsServers)? GetPrimaryInterfaceDns()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new { Interface = n, Props = n.GetIPProperties() })
                .Where(x => x.Props.GatewayAddresses.Count > 0)
                .OrderByDescending(x => x.Interface.Speed)
                .FirstOrDefault();

            if (candidate == null)
                return null;

            var dns = candidate.Props.DnsAddresses
                .Select(a => a.ToString())
                .ToList();

            return (candidate.Interface.Name, dns);
        }
        catch
        {
            // Best-effort status display - never let this crash the menu
            return null;
        }
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AstroPostMaster.Handoff;

public sealed record NetworkCandidate(IPAddress Address, string InterfaceName, NetworkInterfaceType Type, bool HasGateway, bool IsUp);

/// <summary>Chooses the IPv4 address a phone on the same Wi-Fi can reach, ignoring Docker/VM/VPN adapters.</summary>
public static class LanAddress
{
    private static readonly string[] VirtualPrefixes =
        ["docker", "br-", "veth", "virbr", "vmnet", "vboxnet", "tailscale", "zt", "utun", "tun", "tap", "wg", "vEthernet", "lxc", "lxd"];

    public static IReadOnlyList<IPAddress> Best() => Rank(Discover());

    /// <summary>The interface carrying <paramref name="address"/> and the prefix length of its network.</summary>
    public static (string InterfaceName, int PrefixLength)? InterfaceOf(IPAddress address)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                if (unicast.Address.Equals(address)) return (nic.Name, unicast.PrefixLength);
        return null;
    }

    public static IReadOnlyList<NetworkCandidate> Discover()
    {
        var result = new List<NetworkCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in props.UnicastAddresses)
                result.Add(new NetworkCandidate(unicast.Address, nic.Name, nic.NetworkInterfaceType, hasGateway,
                    nic.OperationalStatus == OperationalStatus.Up));
        }
        return result;
    }

    public static IReadOnlyList<IPAddress> Rank(IEnumerable<NetworkCandidate> candidates) =>
        candidates
            .Where(c => c.IsUp
                && c.Address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(c.Address)
                && !IsLinkLocal(c.Address)
                && !IsVirtual(c))
            .OrderByDescending(c => c.HasGateway)
            .ThenByDescending(c => IsPrivate(c.Address))
            .ThenByDescending(c => c.Type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .ThenBy(c => c.InterfaceName, StringComparer.Ordinal)
            .Select(c => c.Address)
            .Distinct()
            .ToList();

    private static bool IsVirtual(NetworkCandidate c) =>
        c.Type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel
        || c.InterfaceName == "lo"
        || VirtualPrefixes.Any(p => c.InterfaceName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsLinkLocal(IPAddress a) => a.GetAddressBytes() is [169, 254, ..];

    private static bool IsPrivate(IPAddress a) => a.GetAddressBytes() switch
    {
        [10, ..] => true,
        [172, var b, ..] when b is >= 16 and <= 31 => true,
        [192, 168, ..] => true,
        _ => false,
    };
}

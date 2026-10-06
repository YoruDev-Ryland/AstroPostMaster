using System.Net;
using System.Net.NetworkInformation;

namespace AstroPostMaster.Handoff.Tests;

public class LanAddressTests
{
    private static NetworkCandidate C(string ip, string name, bool gateway, NetworkInterfaceType type = NetworkInterfaceType.Ethernet, bool up = true) =>
        new(IPAddress.Parse(ip), name, type, gateway, up);

    [Fact]
    public void Rank_PrefersGatewayLanOverDockerAndVpn()
    {
        var ranked = LanAddress.Rank(
        [
            C("172.17.0.1", "docker0", gateway: false),
            C("10.8.0.2", "wg0", gateway: true, NetworkInterfaceType.Unknown),
            C("100.101.1.2", "tailscale0", gateway: false, NetworkInterfaceType.Unknown),
            C("192.168.122.1", "virbr0", gateway: false),
            C("127.0.0.1", "lo", gateway: false, NetworkInterfaceType.Loopback),
            C("169.254.10.10", "enp5s0", gateway: false),
            C("fe80::1", "wlan0", gateway: true, NetworkInterfaceType.Wireless80211),
            C("192.168.1.50", "wlan0", gateway: true, NetworkInterfaceType.Wireless80211),
        ]);

        Assert.Equal(new[] { IPAddress.Parse("192.168.1.50") }, ranked);
    }

    [Fact]
    public void Rank_KeepsPrivateAddressWithoutGatewayAsFallback()
    {
        var ranked = LanAddress.Rank([C("10.0.0.5", "enp4s0", gateway: false), C("192.168.1.9", "enp5s0", gateway: true)]);
        Assert.Equal(new[] { IPAddress.Parse("192.168.1.9"), IPAddress.Parse("10.0.0.5") }, ranked);
    }

    [Fact]
    public void Rank_SkipsInterfacesThatAreDown() =>
        Assert.Empty(LanAddress.Rank([C("192.168.1.9", "enp5s0", gateway: true, up: false)]));

    [Fact]
    public void Rank_HandlesWindowsInterfaceNames()
    {
        var ranked = LanAddress.Rank(
        [
            C("172.28.0.1", "vEthernet (WSL)", gateway: false),
            C("192.168.0.20", "Local Area Connection", gateway: true),
        ]);
        Assert.Equal(new[] { IPAddress.Parse("192.168.0.20") }, ranked);
    }

    [Fact]
    public void Discover_DoesNotThrowOnThisMachine() => Assert.NotNull(LanAddress.Discover());
}

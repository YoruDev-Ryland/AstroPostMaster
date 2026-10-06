using System.Net;
using System.Text.RegularExpressions;

namespace AstroPostMaster.Handoff.Firewall;

/// <summary>
/// Evaluates ufw's own rule files (world-readable, no root needed). ufw applies user rules in order, first
/// match wins, then the default input policy.
/// </summary>
public static partial class UfwRules
{
    private sealed record Rule(string Action, string Proto, string DPort, string Dst, string Src, string Direction);

    [GeneratedRegex(@"^\s*ENABLED\s*=\s*yes\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex EnabledRegex();

    [GeneratedRegex(@"DEFAULT_INPUT_POLICY\s*=\s*""?(\w+)""?")]
    private static partial Regex PolicyRegex();

    public static bool IsEnabled(string? ufwConf) => ufwConf is not null && EnabledRegex().IsMatch(ufwConf);

    /// <param name="prefixLength">The LAN's prefix; a rule must admit the whole LAN, not just one other device.</param>
    public static Reachability Evaluate(string? ufwConf, string? defaults, string? userRules, int port, IPAddress lanAddress, int prefixLength)
    {
        if (!IsEnabled(ufwConf)) return Reachability.Open;
        if (userRules is null) return Reachability.Unknown;

        foreach (var rule in Parse(userRules))
        {
            if (!rule.Direction.StartsWith("in", StringComparison.Ordinal)) continue;
            if (rule.Proto is not ("tcp" or "any")) continue;
            if (!PortMatches(rule.DPort, port)) continue;
            if (!Net.Covers(rule.Dst, lanAddress, 32)) continue;
            if (!Net.Covers(rule.Src, lanAddress, prefixLength)) continue;
            return rule.Action is "allow" or "limit" ? Reachability.Open : Reachability.Blocked;
        }

        var policy = PolicyRegex().Match(defaults ?? "");
        if (!policy.Success) return Reachability.Unknown;
        return policy.Groups[1].Value.Equals("ACCEPT", StringComparison.OrdinalIgnoreCase) ? Reachability.Open : Reachability.Blocked;
    }

    private static IEnumerable<Rule> Parse(string userRules)
    {
        foreach (var line in userRules.Split('\n'))
        {
            const string marker = "### tuple ###";
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            var t = line[(at + marker.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // action proto dport dst sport src [dapp sapp] direction [comment=<hex>]
            if (t.Length < 7) continue;
            var direction = t.Skip(6).LastOrDefault(x => x.StartsWith("in", StringComparison.Ordinal) || x.StartsWith("out", StringComparison.Ordinal));
            if (direction is null) continue;
            yield return new Rule(t[0], t[1], t[2], t[3], t[5], direction);
        }
    }

    private static bool PortMatches(string spec, int port)
    {
        if (spec == "any") return true;
        foreach (var part in spec.Split(','))
        {
            var range = part.Split(':');
            if (range.Length == 1 && int.TryParse(range[0], out var single) && single == port) return true;
            if (range.Length == 2 && int.TryParse(range[0], out var lo) && int.TryParse(range[1], out var hi) && port >= lo && port <= hi) return true;
        }
        return false;
    }
}

internal static class Net
{
    /// <summary>True when the CIDR <paramref name="spec"/> ("0.0.0.0/0", "192.168.1.0/24", "10.0.0.5") contains the
    /// whole network <paramref name="address"/>/<paramref name="prefixLength"/>.</summary>
    public static bool Covers(string spec, IPAddress address, int prefixLength)
    {
        if (spec is "any") return true;
        var parts = spec.Split('/');
        if (!IPAddress.TryParse(parts[0], out var ruleAddress) || ruleAddress.AddressFamily != address.AddressFamily) return false;
        var rulePrefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 32;
        if (rulePrefix > prefixLength) return false;
        return Mask(ruleAddress, rulePrefix) == Mask(address, rulePrefix);
    }

    public static string NetworkCidr(IPAddress address, int prefixLength)
    {
        var masked = Mask(address, prefixLength);
        var bytes = BitConverter.GetBytes(masked);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return $"{new IPAddress(bytes)}/{prefixLength}";
    }

    private static uint Mask(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var value = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        return prefixLength == 0 ? 0 : value & (uint.MaxValue << (32 - prefixLength));
    }
}

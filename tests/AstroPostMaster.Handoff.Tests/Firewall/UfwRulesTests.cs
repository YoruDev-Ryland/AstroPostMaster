using System.Net;
using AstroPostMaster.Handoff.Firewall;

namespace AstroPostMaster.Handoff.Tests.Firewall;

public class UfwRulesTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");
    private const string Enabled = "ENABLED=yes\nLOGLEVEL=low\n";
    private const string DropPolicy = "IPV6=yes\nDEFAULT_INPUT_POLICY=\"DROP\"\nDEFAULT_OUTPUT_POLICY=\"ACCEPT\"\n";

    // A typical desktop setup: several rules, none for 48443.
    private const string RealRules = """
        *filter
        ### RULES ###
        ### tuple ### allow any 22 0.0.0.0/0 any 0.0.0.0/0 in
        -A ufw-user-input -p tcp --dport 22 -j ACCEPT
        ### tuple ### allow tcp 8000 0.0.0.0/0 any 0.0.0.0/0 in
        ### tuple ### allow tcp 48000:48010 0.0.0.0/0 any 0.0.0.0/0 in
        ### tuple ### allow tcp 9000 0.0.0.0/0 any 192.168.1.30 in
        ### END RULES ###
        """;

    private static Reachability Eval(string userRules, string conf = Enabled, string defaults = DropPolicy) =>
        UfwRules.Evaluate(conf, defaults, userRules, 48443, Lan, 24);

    [Fact]
    public void DefaultDropWithoutAMatchingRule_IsBlocked() => Assert.Equal(Reachability.Blocked, Eval(RealRules));

    [Theory]
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 192.168.1.0/24 in")]
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 0.0.0.0/0 in")]
    [InlineData("### tuple ### allow any 48443 0.0.0.0/0 any 0.0.0.0/0 in")]
    [InlineData("### tuple ### allow tcp 48000:49000 0.0.0.0/0 any 0.0.0.0/0 in")]
    [InlineData("### tuple ### allow tcp 80,443,48443 0.0.0.0/0 any 0.0.0.0/0 in")]
    [InlineData("### tuple ### allow tcp 48443 192.168.1.50 any 192.168.0.0/16 in")]
    [InlineData("### tuple ### limit tcp 48443 0.0.0.0/0 any 0.0.0.0/0 in")]
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 0.0.0.0/0 AstroPostMaster - in")]
    // What ufw writes for the app's own fix (rule comment is hex-encoded after the direction).
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 192.168.1.0/24 in comment=417374726f506f73744d61737465722070686f6e65")]
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 192.168.1.0/24 in_enp11s0 comment=41")]
    public void AMatchingAllowRule_OpensThePort(string rule) =>
        Assert.Equal(Reachability.Open, Eval(RealRules.Replace("### END RULES ###", rule + "\n### END RULES ###")));

    [Theory]
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 192.168.1.30 in")]    // one other host only
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 10.0.0.0/8 in")]       // a different network
    [InlineData("### tuple ### allow udp 48443 0.0.0.0/0 any 0.0.0.0/0 in")]        // wrong protocol
    [InlineData("### tuple ### allow tcp 48443 0.0.0.0/0 any 0.0.0.0/0 out")]       // wrong direction
    public void RulesThatDontCoverPhonesOnTheLan_DontCount(string rule) =>
        Assert.Equal(Reachability.Blocked, Eval(rule));

    [Fact]
    public void AnEarlierDenyWins() =>
        Assert.Equal(Reachability.Blocked, Eval("""
            ### tuple ### deny tcp 48443 0.0.0.0/0 any 0.0.0.0/0 in
            ### tuple ### allow tcp 48443 0.0.0.0/0 any 0.0.0.0/0 in
            """));

    [Fact]
    public void DisabledUfw_IsOpen() => Assert.Equal(Reachability.Open, Eval(RealRules, conf: "ENABLED=no\n"));

    [Fact]
    public void AcceptPolicy_IsOpen() => Assert.Equal(Reachability.Open, Eval(RealRules, defaults: "DEFAULT_INPUT_POLICY=\"ACCEPT\"\n"));

    [Fact]
    public void UnreadableRules_AreUnknown() =>
        Assert.Equal(Reachability.Unknown, UfwRules.Evaluate(Enabled, DropPolicy, null, 48443, Lan, 24));
}

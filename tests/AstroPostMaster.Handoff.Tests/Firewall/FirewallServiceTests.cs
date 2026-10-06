using System.Net;
using System.Text;
using AstroPostMaster.Handoff.Firewall;

namespace AstroPostMaster.Handoff.Tests.Firewall;

/// <summary>Records commands and answers from a script, like a shell would.</summary>
internal sealed class FakeRunner : ICommandRunner
{
    public List<(string File, IReadOnlyList<string> Args, bool Elevated)> Calls { get; } = [];
    public Func<string, IReadOnlyList<string>, bool, CommandResult?> Respond { get; set; } = (_, _, _) => null;

    public Task<CommandResult?> RunAsync(string fileName, IReadOnlyList<string> args, bool elevated = false, CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, args, elevated));
        return Task.FromResult(Respond(fileName, args, elevated));
    }
}

public class FirewallServiceTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");
    private static (string, int)? LanInfo(IPAddress _) => ("enp11s0", 24);
    private const string Exe = "/opt/AstroPostMaster/AstroPostMaster";

    private static FirewallService Linux(FakeRunner runner, Dictionary<string, string> files) =>
        new(runner, HostPlatform.Linux, path => files.GetValueOrDefault(path), LanInfo, Exe);

    private static readonly Dictionary<string, string> UfwBlocking = new()
    {
        ["/etc/ufw/ufw.conf"] = "ENABLED=yes\n",
        ["/etc/default/ufw"] = "DEFAULT_INPUT_POLICY=\"DROP\"\n",
        ["/etc/ufw/user.rules"] = "### tuple ### allow tcp 22 0.0.0.0/0 any 0.0.0.0/0 in\n",
    };

    // ---------- Linux: ufw ----------

    [Fact]
    public async Task Ufw_Blocking_OffersAPkexecFixForTheSubnetOnly()
    {
        var service = Linux(new FakeRunner(), UfwBlocking);

        var status = await service.CheckAsync(48443, Lan);

        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Equal("ufw", status.Name);
        var fix = Assert.IsType<FirewallFix>(status.Fix);
        Assert.Equal("pkexec", fix.FileName);
        Assert.Equal(new[] { "ufw", "allow", "from", "192.168.1.0/24", "to", "any", "port", "48443", "proto", "tcp", "comment", "AstroPostMaster phone" }, fix.Arguments);
        Assert.False(fix.Elevated);
        Assert.Equal("sudo ufw allow from 192.168.1.0/24 to any port 48443 proto tcp", fix.ManualCommand);
        Assert.Contains("192.168.1.0/24", fix.Summary);
    }

    [Fact]
    public async Task Ufw_Open_HasNoFix()
    {
        var files = new Dictionary<string, string>(UfwBlocking) { ["/etc/ufw/user.rules"] = "### tuple ### allow tcp 48443 0.0.0.0/0 any 192.168.1.0/24 in\n" };
        var status = await Linux(new FakeRunner(), files).CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Open, status.State);
        Assert.Null(status.Fix);
    }

    // ---------- Linux: firewalld ----------

    private static FakeRunner Firewalld(bool portOpen, string zone = "public") => new()
    {
        Respond = (file, args, _) => (file, args.FirstOrDefault()) switch
        {
            ("firewall-cmd", "--state") => new CommandResult(0, "running\n", ""),
            ("firewall-cmd", "--get-zone-of-interface=enp11s0") => new CommandResult(0, zone + "\n", ""),
            ("firewall-cmd", var a) when a == $"--zone={zone}" && args.Contains("--query-port=48443/tcp") => new CommandResult(portOpen ? 0 : 1, portOpen ? "yes" : "no", ""),
            ("firewall-cmd", var a) when a == $"--zone={zone}" && args.Contains("--list-rich-rules") => new CommandResult(0, "", ""),
            _ => null,
        },
    };

    [Fact]
    public async Task Firewalld_Blocking_OffersARichRuleForTheSubnet()
    {
        var status = await Linux(Firewalld(portOpen: false), []).CheckAsync(48443, Lan);

        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Equal("firewalld", status.Name);
        var fix = status.Fix!;
        Assert.Equal("pkexec", fix.FileName);
        Assert.Equal("sh", fix.Arguments[0]);
        Assert.Contains("--zone=public --add-rich-rule=", fix.Arguments[2]);
        Assert.Contains("--permanent", fix.Arguments[2]);
        Assert.Contains("source address=\"192.168.1.0/24\" port port=\"48443\" protocol=\"tcp\" accept", fix.Arguments[2]);
    }

    [Fact]
    public async Task Firewalld_PortAlreadyOpen_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Linux(Firewalld(portOpen: true), []).CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Firewalld_TrustedZone_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Linux(Firewalld(portOpen: false, zone: "trusted"), []).CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Linux_WithoutUfwOrFirewalld_IsOpen()
    {
        var status = await Linux(new FakeRunner(), []).CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Open, status.State);
        Assert.Contains("No firewall", status.Message);
    }

    // ---------- Windows ----------

    private static FirewallService Windows(string reportJson) =>
        new(new FakeRunner { Respond = (_, _, _) => new CommandResult(0, reportJson, "") }, HostPlatform.Windows,
            _ => null, _ => ("Ethernet", 24), @"C:\Apps\AstroPostMaster.exe");

    private static string Report(string category, bool enabled = true, string defaultInbound = "NotConfigured", string rules = "") =>
        $$"""{"Profiles":[{"Name":"Domain","Enabled":true,"DefaultInbound":"NotConfigured"},{"Name":"Private","Enabled":{{(category == "Private" ? enabled : true).ToString().ToLowerInvariant()}},"DefaultInbound":"{{defaultInbound}}"},{"Name":"Public","Enabled":{{(category == "Public" ? enabled : true).ToString().ToLowerInvariant()}},"DefaultInbound":"{{defaultInbound}}"}],"Networks":[{"Alias":"Tailscale","Category":"Public"},{"Alias":"Ethernet","Category":"{{category}}"}],"Rules":[{{rules}}]}""";

    [Fact]
    public async Task Windows_NoRule_IsBlocked_AndOffersAnElevatedFix()
    {
        var status = await Windows(Report("Private")).CheckAsync(48443, Lan);

        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Equal("Windows Defender Firewall", status.Name);
        var fix = status.Fix!;
        Assert.Equal("powershell.exe", fix.FileName);
        Assert.True(fix.Elevated);
        var encoded = fix.Arguments[fix.Arguments.ToList().IndexOf("-EncodedCommand") + 1];
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains("New-NetFirewallRule", script);
        Assert.Contains("-LocalPort 48443", script);
        Assert.Contains("-RemoteAddress LocalSubnet", script);
        Assert.Contains("Remove-NetFirewallRule", script); // clears "block" rules left by a dismissed Windows prompt
        Assert.Contains("New-NetFirewallRule", fix.ManualCommand);
    }

    [Fact]
    public async Task Windows_AllowRuleForTheActiveProfile_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Windows(Report("Private", rules: """{"Name":"AstroPostMaster (phone)","Action":"Allow","Profile":"Any"}""")).CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Windows_BlockRuleBeatsAllowRule() =>
        Assert.Equal(Reachability.Blocked, (await Windows(Report("Private", rules: """{"Name":"a","Action":"Allow","Profile":"Any"},{"Name":"AstroPostMaster","Action":"Block","Profile":"Private, Public"}""")).CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Windows_AllowOnlyOnPrivate_WhileOnAPublicNetwork_IsBlocked()
    {
        var status = await Windows(Report("Public", rules: """{"Name":"AstroPostMaster","Action":"Allow","Profile":"Private"}""")).CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Contains("Public", status.Message);
    }

    [Fact]
    public async Task Windows_FirewallOffForTheNetwork_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Windows(Report("Private", enabled: false)).CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Windows_ReportFailure_IsUnknownButStillFixable()
    {
        var service = new FirewallService(new FakeRunner(), HostPlatform.Windows, _ => null, _ => ("Ethernet", 24), @"C:\a.exe");
        var status = await service.CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Unknown, status.State);
        Assert.NotNull(status.Fix);
    }

    // ---------- macOS ----------

    private static FirewallService Mac(string globalState, string blockAll, string app) =>
        new(new FakeRunner
        {
            Respond = (_, args, _) => args[0] switch
            {
                "--getglobalstate" => new CommandResult(0, globalState, ""),
                "--getblockall" => new CommandResult(0, blockAll, ""),
                "--getappblocked" => new CommandResult(0, app, ""),
                _ => null,
            },
        }, HostPlatform.MacOS, _ => null, _ => ("en0", 24), "/Applications/AstroPostMaster");

    [Fact]
    public async Task Mac_FirewallOff_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Mac("Firewall is disabled. (State = 0)", "", "").CheckAsync(48443, Lan)).State);

    [Fact]
    public async Task Mac_BlockAll_IsBlocked_WithInstructionsInsteadOfAFix()
    {
        var status = await Mac("Firewall is enabled. (State = 1)", "Firewall has block all state set to enabled.", "").CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Null(status.Fix);
        Assert.Contains("Block all incoming connections", status.Message);
    }

    [Fact]
    public async Task Mac_AppBlocked_OffersAnAdminFix()
    {
        var status = await Mac("Firewall is enabled. (State = 1)", "Firewall has block all state set to disabled.",
            "The application /Applications/AstroPostMaster is blocking incoming connections.").CheckAsync(48443, Lan);
        Assert.Equal(Reachability.Blocked, status.State);
        Assert.Equal("osascript", status.Fix!.FileName);
        Assert.Contains("with administrator privileges", status.Fix.Arguments[1]);
        Assert.Contains("--unblockapp", status.Fix.Arguments[1]);
    }

    [Fact]
    public async Task Mac_AppPermitted_IsOpen() =>
        Assert.Equal(Reachability.Open, (await Mac("Firewall is enabled. (State = 1)", "Firewall has block all state set to disabled.",
            "The application /Applications/AstroPostMaster is permitted to accept incoming connections.").CheckAsync(48443, Lan)).State);

    // ---------- applying fixes ----------

    [Theory]
    [InlineData(0, false, FixOutcome.Applied)]
    [InlineData(126, false, FixOutcome.Cancelled)]   // pkexec dialog dismissed
    [InlineData(127, false, FixOutcome.Failed)]      // pkexec authentication failed
    [InlineData(-1, true, FixOutcome.Cancelled)]     // UAC declined
    [InlineData(1, false, FixOutcome.Failed)]
    public async Task Fix_MapsExitCodes(int exitCode, bool cancelled, FixOutcome expected)
    {
        var runner = new FakeRunner { Respond = (_, _, _) => new CommandResult(exitCode, "", "", cancelled) };
        var service = new FirewallService(runner, HostPlatform.Linux, _ => null, LanInfo, Exe);
        var fix = new FirewallFix("Allow", "sudo ufw allow 48443", "pkexec", ["ufw", "allow", "48443"], Elevated: false);

        var result = await service.FixAsync(fix);

        Assert.Equal(expected, result.Outcome);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Fix_MacUserCancelled_IsCancelled()
    {
        var runner = new FakeRunner { Respond = (_, _, _) => new CommandResult(1, "", "execution error: User canceled. (-128)") };
        var service = new FirewallService(runner, HostPlatform.MacOS, _ => null, LanInfo, Exe);
        var result = await service.FixAsync(new FirewallFix("x", "x", "osascript", ["-e", "x"], false));
        Assert.Equal(FixOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task Fix_MissingTool_FailsWithTheManualCommand()
    {
        var service = new FirewallService(new FakeRunner(), HostPlatform.Linux, _ => null, LanInfo, Exe);
        var result = await service.FixAsync(new FirewallFix("x", "sudo ufw allow 48443", "pkexec", ["ufw"], false));
        Assert.Equal(FixOutcome.Failed, result.Outcome);
        Assert.Contains("sudo ufw allow 48443", result.Message);
    }

    [Fact]
    public void LanInfo_FindsTheInterfaceAndPrefixOfAnAddress()
    {
        var info = LanAddress.InterfaceOf(IPAddress.Loopback);
        Assert.NotNull(info);
        Assert.Equal(8, info.Value.PrefixLength);
    }
}

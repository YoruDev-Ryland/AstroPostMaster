using System.Net;
using System.Runtime.InteropServices;

namespace AstroPostMaster.Handoff.Firewall;

/// <summary>
/// Figures out whether the local firewall lets phones on the LAN reach the handoff server, explains the result in
/// plain words, and offers a narrowly scoped fix (this port, this LAN subnet) that the user approves through the
/// operating system's own password/admin prompt.
/// </summary>
public sealed class FirewallService(
    ICommandRunner runner,
    HostPlatform platform,
    Func<string, string?> readFile,
    Func<IPAddress, (string InterfaceName, int PrefixLength)?> lanInfo,
    string exePath) : IFirewall
{
    private const string MacFirewall = "/usr/libexec/ApplicationFirewall/socketfilterfw";
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    public static FirewallService ForCurrentSystem() => new(
        new ProcessCommandRunner(),
        OperatingSystem.IsWindows() ? HostPlatform.Windows : OperatingSystem.IsMacOS() ? HostPlatform.MacOS : HostPlatform.Linux,
        path => { try { return File.Exists(path) ? File.ReadAllText(path) : null; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } },
        LanAddress.InterfaceOf,
        Environment.ProcessPath ?? "AstroPostMaster");

    public async Task<FirewallStatus> CheckAsync(int port, IPAddress address, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        var info = lanInfo(address) ?? ("", 24);
        try
        {
            return platform switch
            {
                HostPlatform.Windows => await CheckWindowsAsync(port, info.InterfaceName, timeout.Token),
                HostPlatform.MacOS => await CheckMacAsync(timeout.Token),
                _ => await CheckLinuxAsync(port, address, info.InterfaceName, info.PrefixLength, timeout.Token),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FirewallStatus("Firewall", Reachability.Unknown, "Checking the firewall took too long.");
        }
    }

    public async Task<FixResult> FixAsync(FirewallFix fix, CancellationToken cancellationToken = default)
    {
        var result = await runner.RunAsync(fix.FileName, fix.Arguments, fix.Elevated, cancellationToken);
        if (result is null)
            return new(FixOutcome.Failed, $"Couldn't run {fix.FileName}. Run this yourself instead:\n{fix.ManualCommand}");
        if (result.Cancelled || (platform == HostPlatform.Linux && result.ExitCode == 126) || result.StdErr.Contains("-128", StringComparison.Ordinal))
            return new(FixOutcome.Cancelled, "Cancelled. Nothing was changed.");
        if (result.ExitCode != 0)
            return new(FixOutcome.Failed, $"The firewall change didn't go through. You can run this yourself:\n{fix.ManualCommand}");
        return new(FixOutcome.Applied, "Firewall rule added.");
    }

    // ---------- Linux ----------

    private async Task<FirewallStatus> CheckLinuxAsync(int port, IPAddress address, string iface, int prefix, CancellationToken ct)
    {
        var subnet = Net.NetworkCidr(address, prefix);
        var ufwConf = readFile("/etc/ufw/ufw.conf");
        if (UfwRules.IsEnabled(ufwConf))
        {
            var state = UfwRules.Evaluate(ufwConf, readFile("/etc/default/ufw"), readFile("/etc/ufw/user.rules"), port, address, prefix);
            var fix = new FirewallFix(
                $"Adds a ufw rule that lets devices on your home network ({subnet}) reach port {port}.",
                $"sudo ufw allow from {subnet} to any port {port} proto tcp",
                "pkexec",
                ["ufw", "allow", "from", subnet, "to", "any", "port", port.ToString(), "proto", "tcp", "comment", "AstroPostMaster phone"],
                Elevated: false);
            return state switch
            {
                Reachability.Open => new("ufw", state, "Your firewall (ufw) allows phones on your network to connect."),
                Reachability.Blocked => new("ufw", state, $"Your firewall (ufw) is blocking phones from reaching port {port}.", fix),
                _ => new("ufw", state, "Your firewall (ufw) is on, but its rules couldn't be read.", fix),
            };
        }

        var running = await runner.RunAsync("firewall-cmd", ["--state"], false, ct);
        if (running is { ExitCode: 0 })
        {
            var zoneResult = await runner.RunAsync("firewall-cmd", [$"--get-zone-of-interface={iface}"], false, ct);
            var zone = zoneResult is { ExitCode: 0 } ? zoneResult.StdOut.Trim() : "";
            if (zone.Length == 0)
                zone = (await runner.RunAsync("firewall-cmd", ["--get-default-zone"], false, ct))?.StdOut.Trim() ?? "public";

            var rich = $"rule family=\"ipv4\" source address=\"{subnet}\" port port=\"{port}\" protocol=\"tcp\" accept";
            var portOpen = await runner.RunAsync("firewall-cmd", [$"--zone={zone}", $"--query-port={port}/tcp"], false, ct);
            var richRules = await runner.RunAsync("firewall-cmd", [$"--zone={zone}", "--list-rich-rules"], false, ct);
            var open = zone == "trusted" || portOpen is { ExitCode: 0 } || (richRules?.StdOut.Contains($"port port=\"{port}\"", StringComparison.Ordinal) ?? false);
            if (open) return new("firewalld", Reachability.Open, "Your firewall (firewalld) allows phones on your network to connect.");

            var script = $"firewall-cmd --zone={zone} --add-rich-rule='{rich}' && firewall-cmd --permanent --zone={zone} --add-rich-rule='{rich}'";
            return new("firewalld", Reachability.Blocked, $"Your firewall (firewalld, zone \"{zone}\") is blocking phones from reaching port {port}.",
                new FirewallFix(
                    $"Adds a firewalld rule that lets devices on your home network ({subnet}) reach port {port}.",
                    $"sudo sh -c \"{script.Replace("\"", "\\\"")}\"",
                    "pkexec", ["sh", "-c", script], Elevated: false));
        }

        return new("none", Reachability.Open, "No firewall (ufw or firewalld) is blocking phones on this computer.");
    }

    // ---------- Windows ----------

    private async Task<FirewallStatus> CheckWindowsAsync(int port, string iface, CancellationToken ct)
    {
        const string name = "Windows Defender Firewall";
        var fixScript = WindowsFirewallReport.FixScript(port, exePath);
        var fix = new FirewallFix(
            $"Adds a Windows Firewall rule that lets devices on your local network reach port {port}, and removes any rule blocking AstroPostMaster.",
            $"In PowerShell (Run as administrator):\n{fixScript.Trim()}",
            "powershell.exe", WindowsFirewallReport.PowerShellArgs(fixScript), Elevated: true);

        var report = await runner.RunAsync("powershell.exe", WindowsFirewallReport.PowerShellArgs(WindowsFirewallReport.Script(port, exePath)), false, ct);
        if (report is not { ExitCode: 0 } || string.IsNullOrWhiteSpace(report.StdOut))
            return new(name, Reachability.Unknown, "Couldn't read the Windows Firewall settings.", fix);

        try
        {
            var (state, message) = WindowsFirewallReport.Evaluate(report.StdOut, iface);
            return new(name, state, message, state == Reachability.Open ? null : fix);
        }
        catch (System.Text.Json.JsonException)
        {
            return new(name, Reachability.Unknown, "Couldn't read the Windows Firewall settings.", fix);
        }
    }

    // ---------- macOS ----------

    private async Task<FirewallStatus> CheckMacAsync(CancellationToken ct)
    {
        const string name = "macOS Firewall";
        var global = await runner.RunAsync(MacFirewall, ["--getglobalstate"], false, ct);
        if (global is null) return new(name, Reachability.Unknown, "Couldn't read the macOS firewall settings.");
        if (global.StdOut.Contains("disabled", StringComparison.OrdinalIgnoreCase))
            return new(name, Reachability.Open, "The macOS firewall is off.");

        var blockAll = await runner.RunAsync(MacFirewall, ["--getblockall"], false, ct);
        if (blockAll?.StdOut.Contains("enabled", StringComparison.OrdinalIgnoreCase) == true)
            return new(name, Reachability.Blocked,
                "The macOS firewall is set to \"Block all incoming connections\". Turn that off in System Settings → Network → Firewall → Options.");

        var shell = $"{MacFirewall} --add '{exePath}' && {MacFirewall} --unblockapp '{exePath}'";
        var fix = new FirewallFix(
            "Lets AstroPostMaster accept connections through the macOS firewall.",
            $"sudo sh -c \"{shell}\"",
            "osascript", ["-e", $"do shell script \"{shell}\" with administrator privileges"], Elevated: false);

        var app = await runner.RunAsync(MacFirewall, ["--getappblocked", exePath], false, ct);
        var text = app?.StdOut ?? "";
        if (text.Contains("permitted", StringComparison.OrdinalIgnoreCase))
            return new(name, Reachability.Open, "The macOS firewall allows AstroPostMaster.");
        if (text.Contains("block", StringComparison.OrdinalIgnoreCase))
            return new(name, Reachability.Blocked, "The macOS firewall is blocking AstroPostMaster.", fix);
        return new(name, Reachability.Unknown, "The macOS firewall is on. If macOS asks whether to accept incoming connections, choose Allow.", fix);
    }
}

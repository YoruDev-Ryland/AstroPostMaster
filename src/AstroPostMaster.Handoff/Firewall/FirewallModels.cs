namespace AstroPostMaster.Handoff.Firewall;

public enum Reachability { Open, Blocked, Unknown }

public enum HostPlatform { Linux, Windows, MacOS }

/// <param name="Name">Firewall in charge, e.g. "ufw", "Windows Defender Firewall".</param>
/// <param name="Message">Plain-language explanation for the user.</param>
/// <param name="Fix">A change the app can make with the user's consent, or null.</param>
public sealed record FirewallStatus(string Name, Reachability State, string Message, FirewallFix? Fix = null);

/// <param name="Summary">What the fix changes, shown before the user consents.</param>
/// <param name="ManualCommand">The same change as a command to run by hand.</param>
/// <param name="Elevated">Run through the OS elevation prompt (Windows UAC). Linux/macOS fixes elevate via pkexec/osascript themselves.</param>
public sealed record FirewallFix(string Summary, string ManualCommand, string FileName, IReadOnlyList<string> Arguments, bool Elevated);

public enum FixOutcome { Applied, Cancelled, Failed }

public sealed record FixResult(FixOutcome Outcome, string Message);

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool Cancelled = false);

public interface IFirewall
{
    /// <summary>Whether a phone on the LAN of <paramref name="address"/> can reach <paramref name="port"/>.</summary>
    Task<FirewallStatus> CheckAsync(int port, System.Net.IPAddress address, CancellationToken cancellationToken = default);

    Task<FixResult> FixAsync(FirewallFix fix, CancellationToken cancellationToken = default);
}

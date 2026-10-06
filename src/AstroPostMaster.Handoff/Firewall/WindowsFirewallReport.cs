using System.Text;
using System.Text.Json;

namespace AstroPostMaster.Handoff.Firewall;

/// <summary>
/// Reads a locale-independent JSON report from PowerShell's NetSecurity cmdlets and decides whether the port is
/// reachable on the network the LAN interface is connected to (Windows applies Public/Private/Domain profiles).
/// Block rules beat allow rules; with no matching rule the profile's default inbound action applies (Block).
/// </summary>
public static class WindowsFirewallReport
{
    public static string Script(int port, string exePath) => $$"""
        $ErrorActionPreference = 'SilentlyContinue'
        $port = '{{port}}'; $exe = '{{exePath.Replace("'", "''")}}'
        $profiles = @(Get-NetFirewallProfile -PolicyStore ActiveStore | ForEach-Object {
          [pscustomobject]@{ Name = "$($_.Name)"; Enabled = ("$($_.Enabled)" -eq 'True'); DefaultInbound = "$($_.DefaultInboundAction)" } })
        $networks = @(Get-NetConnectionProfile | ForEach-Object { [pscustomobject]@{ Alias = $_.InterfaceAlias; Category = "$($_.NetworkCategory)" } })
        $rules = @(Get-NetFirewallRule -Direction Inbound -Enabled True -PolicyStore ActiveStore | ForEach-Object {
          $r = $_; $pf = $r | Get-NetFirewallPortFilter; $af = $r | Get-NetFirewallApplicationFilter
          $portOk = ($pf.Protocol -in 'TCP','Any') -and (($pf.LocalPort -contains $port) -or ($pf.LocalPort -contains 'Any'))
          $appOk = ($af.Program -eq 'Any') -or ($af.Program -eq $exe)
          if (($portOk -and $appOk) -or ($af.Program -eq $exe -and $pf.LocalPort -contains 'Any')) {
            [pscustomobject]@{ Name = $r.DisplayName; Action = "$($r.Action)"; Profile = "$($r.Profile)" } } })
        [pscustomobject]@{ Profiles = $profiles; Networks = $networks; Rules = $rules } | ConvertTo-Json -Depth 4 -Compress
        """;

    public static string FixScript(int port, string exePath) => $$"""
        Get-NetFirewallApplicationFilter -Program '{{exePath.Replace("'", "''")}}' -ErrorAction SilentlyContinue | Get-NetFirewallRule | Where-Object { "$($_.Action)" -eq 'Block' -and "$($_.Direction)" -eq 'Inbound' } | Remove-NetFirewallRule
        Get-NetFirewallRule -DisplayName 'AstroPostMaster (phone)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -DisplayName 'AstroPostMaster (phone)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort {{port}} -RemoteAddress LocalSubnet -Profile Any | Out-Null
        """;

    public static IReadOnlyList<string> PowerShellArgs(string script) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))];

    public static (Reachability State, string Message) Evaluate(string json, string interfaceAlias)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var category = Array(root, "Networks")
            .Where(n => Str(n, "Alias").Equals(interfaceAlias, StringComparison.OrdinalIgnoreCase))
            .Select(n => Str(n, "Category"))
            .FirstOrDefault();
        if (category is null) return (Reachability.Unknown, "Couldn't tell which network this computer is on.");
        var profileName = category.StartsWith("Domain", StringComparison.OrdinalIgnoreCase) ? "Domain" : category;

        var profile = Array(root, "Profiles").FirstOrDefault(p => Str(p, "Name").Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile.ValueKind == JsonValueKind.Undefined) return (Reachability.Unknown, "Couldn't read the firewall settings.");
        if (profile.TryGetProperty("Enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            return (Reachability.Open, $"Windows Firewall is off for {profileName} networks.");

        var rules = Array(root, "Rules").Where(r =>
        {
            var profiles = Str(r, "Profile");
            return profiles.Contains("Any", StringComparison.OrdinalIgnoreCase) || profiles.Contains(profileName, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        var network = $"This network is set to {profileName}.";
        if (rules.Any(r => Str(r, "Action") == "Block"))
            return (Reachability.Blocked, $"A Windows Firewall rule is blocking AstroPostMaster. {network}");
        if (rules.Any(r => Str(r, "Action") == "Allow"))
            return (Reachability.Open, $"Windows Firewall allows phones to connect. {network}");
        return Str(profile, "DefaultInbound") == "Allow"
            ? (Reachability.Open, $"Windows Firewall allows incoming connections. {network}")
            : (Reachability.Blocked, $"Windows Firewall is blocking phones from connecting. {network}");
    }

    private static IEnumerable<JsonElement> Array(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

using System.Net;
using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Handoff;
using AstroPostMaster.Handoff.Firewall;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace AstroPostMaster.App.Tests;

internal sealed class FakeFirewall : IFirewall
{
    public Queue<FirewallStatus> Statuses { get; } = new();
    public FixResult FixResult { get; set; } = new(FixOutcome.Applied, "Firewall rule added.");
    public List<(int Port, IPAddress Address)> Checks { get; } = [];
    public List<FirewallFix> Fixes { get; } = [];

    public Task<FirewallStatus> CheckAsync(int port, IPAddress address, CancellationToken ct = default)
    {
        Checks.Add((port, address));
        return Task.FromResult(Statuses.Count > 1 ? Statuses.Dequeue() : Statuses.Peek());
    }

    public Task<FixResult> FixAsync(FirewallFix fix, CancellationToken ct = default)
    {
        Fixes.Add(fix);
        return Task.FromResult(FixResult);
    }
}

public class FirewallUiTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");
    private static readonly FirewallFix UfwFix = new("Adds a ufw rule for 192.168.1.0/24.", "sudo ufw allow ...", "pkexec", ["ufw"], false);
    private static readonly FirewallStatus Blocked = new("ufw", Reachability.Blocked, "Your firewall (ufw) is blocking phones from reaching port 48443.", UfwFix);
    private static readonly FirewallStatus Open = new("ufw", Reachability.Open, "Your firewall (ufw) allows phones on your network to connect.");

    private sealed class Host : IHandoffHost
    {
        public IReadOnlyList<IPAddress> Addresses() => [Lan];
        public Task<IHandoffSession> StartAsync(HandoffPackage p, IPAddress a, bool https, CancellationToken ct = default) =>
            Task.FromResult<IHandoffSession>(new Session(new Uri($"https://{a}:50123/t/")));
    }

    private sealed class Session(Uri url) : IHandoffSession
    {
        public Uri Url { get; } = url;
        public bool HasReceivedRequest => false;
        public event Action? RequestReceived { add { } remove { } }
        public event Action? Stopped { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task<PhoneExportViewModel> Create(TempDir dir, FakeFirewall firewall)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var source = dir.File("src.jpg");
        using (var bmp = new SKBitmap(600, 400))
        using (var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90))
            File.WriteAllBytes(source, data.ToArray());
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(600, 400), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(source);
        return new PhoneExportViewModel(editor, state, new ImmediateDispatcher(), new Host(), new FakeTimeProvider(), firewall);
    }

    [Fact]
    public async Task BlockedFirewall_ShowsAWarningWithAFix_ForTheRealPortAndAddress()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall();
        firewall.Statuses.Enqueue(Blocked);
        var vm = await Create(dir, firewall);

        await vm.StartAsync();
        await vm.FirewallChecking;

        Assert.Equal((50123, Lan), firewall.Checks.Single());
        Assert.True(vm.FirewallBlocked);
        Assert.True(vm.CanFixFirewall);
        Assert.Equal(Blocked.Message, vm.FirewallMessage);
        Assert.Equal(UfwFix.Summary, vm.FirewallFixSummary);
    }

    [Fact]
    public async Task Fixing_AppliesTheFix_AndRechecks()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall();
        firewall.Statuses.Enqueue(Blocked);
        firewall.Statuses.Enqueue(Open);
        var vm = await Create(dir, firewall);
        await vm.StartAsync();
        await vm.FirewallChecking;

        await vm.FixFirewallAsync();

        Assert.Same(UfwFix, firewall.Fixes.Single());
        Assert.Equal(2, firewall.Checks.Count);
        Assert.False(vm.FirewallBlocked);
        Assert.False(vm.ShowFirewallCard); // fixed: nothing left to say
        Assert.Null(vm.FirewallNote);
    }

    [Fact]
    public async Task CancellingTheFix_ChangesNothing()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall { FixResult = new(FixOutcome.Cancelled, "Cancelled — nothing was changed.") };
        firewall.Statuses.Enqueue(Blocked);
        var vm = await Create(dir, firewall);
        await vm.StartAsync();
        await vm.FirewallChecking;

        await vm.FixFirewallAsync();

        Assert.True(vm.FirewallBlocked);
        Assert.Equal("Cancelled — nothing was changed.", vm.FirewallNote);
        Assert.Single(firewall.Checks);
    }

    [Fact]
    public async Task OpenFirewall_ShowsNoWarning()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall();
        firewall.Statuses.Enqueue(Open);
        var vm = await Create(dir, firewall);

        await vm.StartAsync();
        await vm.FirewallChecking;

        Assert.False(vm.FirewallBlocked);
        Assert.False(vm.FirewallUncertain);
        Assert.False(vm.ShowFirewallCard);
    }

    [Fact]
    public async Task UnknownFirewallWithAFix_ShowsASofterHint()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall();
        firewall.Statuses.Enqueue(new FirewallStatus("macOS Firewall", Reachability.Unknown, "If macOS asks, choose Allow.", UfwFix));
        var vm = await Create(dir, firewall);

        await vm.StartAsync();
        await vm.FirewallChecking;

        Assert.False(vm.FirewallBlocked);
        Assert.True(vm.FirewallUncertain);
        Assert.True(vm.CanFixFirewall);
    }

    [Fact]
    public async Task Settings_CheckPhoneConnection_ReportsAndFixes()
    {
        using var dir = new TempDir();
        var firewall = new FakeFirewall();
        firewall.Statuses.Enqueue(Blocked);
        firewall.Statuses.Enqueue(Open);
        var vm = new SettingsViewModel(Sample.State(dir), firewall, () => [Lan]);

        await vm.CheckPhoneConnectionAsync();
        Assert.Equal((Handoff.HandoffServer.DefaultPort, Lan), firewall.Checks.Single());
        Assert.Equal(Blocked.Message, vm.ConnectionStatus);
        Assert.True(vm.CanFixConnection);

        await vm.FixConnectionAsync();
        Assert.Equal("No issues found.", vm.ConnectionStatus);
        Assert.False(vm.CanFixConnection);
    }

    [Fact]
    public async Task Settings_CheckPhoneConnection_WithoutANetwork_SaysSo()
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir), new FakeFirewall(), () => []);

        await vm.CheckPhoneConnectionAsync();

        Assert.Equal("No network connection.", vm.ConnectionStatus);
    }
}

public class HandoffStartFailureTests
{
    private sealed class FailingHost : IHandoffHost
    {
        public IReadOnlyList<IPAddress> Addresses() => [IPAddress.Parse("192.168.1.50")];
        public Task<IHandoffSession> StartAsync(HandoffPackage p, IPAddress a, bool https, CancellationToken ct = default) =>
            throw new System.Security.Cryptography.CryptographicException("Key not valid for use in specified state.");
    }

    [Fact]
    public async Task CertificateFailure_IsReportedInsteadOfCrashing()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var source = dir.File("src.jpg");
        using (var bmp = new SKBitmap(600, 400))
        using (var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90))
            File.WriteAllBytes(source, data.ToArray());
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(600, 400), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(source);
        var vm = new PhoneExportViewModel(editor, state, new ImmediateDispatcher(), new FailingHost(), new FakeTimeProvider());

        await vm.StartAsync();

        Assert.True(vm.HasError);
        Assert.Contains("phone server", vm.Status);
    }
}

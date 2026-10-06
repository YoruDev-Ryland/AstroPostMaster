using System.Net;
using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Handoff;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace AstroPostMaster.App.Tests;

public class PhoneExportViewModelTests
{
    private sealed class FakeSession(Uri url) : IHandoffSession
    {
        public Uri Url { get; } = url;
        public bool HasReceivedRequest { get; private set; }
        public bool Disposed { get; private set; }
        public event Action? RequestReceived;
        public event Action? Stopped;
        public void SimulateRequest() { HasReceivedRequest = true; RequestReceived?.Invoke(); }
        public void SimulateIdleStop() => Stopped?.Invoke();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeHost(params IPAddress[] addresses) : IHandoffHost
    {
        public List<(HandoffPackage Package, IPAddress Address, bool Https, FakeSession Session)> Started { get; } = [];
        public IReadOnlyList<IPAddress> Addresses() => addresses;
        public Task<IHandoffSession> StartAsync(HandoffPackage package, IPAddress address, bool https, CancellationToken ct = default)
        {
            var session = new FakeSession(new Uri($"https://{address}:48443/token/"));
            Started.Add((package, address, https, session));
            return Task.FromResult<IHandoffSession>(session);
        }
    }

    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");
    private static readonly IPAddress Other = IPAddress.Parse("10.0.0.7");

    private static async Task<(PhoneExportViewModel Vm, EditorViewModel Editor, AppState State)> Create(
        TempDir dir, FakeHost host, FakeTimeProvider time, bool realSource = true)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        post.Title = "Heart Nebula";
        state.Store.SavePost(post);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(600, 400), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        var source = dir.File("Heart Nebula.jpg");
        using (var bmp = new SKBitmap(600, 400))
        {
            bmp.Erase(new SKColor(120, 40, 60));
            using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
            File.WriteAllBytes(source, data.ToArray());
        }
        await editor.SetSourceAsync(source);
        editor.AddCropCommand.Execute(null);
        if (!realSource) File.Delete(source);
        return (new PhoneExportViewModel(editor, state, new ImmediateDispatcher(), host, time), editor, state);
    }

    [Fact]
    public async Task Start_ExportsSlides_AndStartsASession()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var (vm, editor, state) = await Create(dir, host, new FakeTimeProvider());

        await vm.StartAsync();

        var started = Assert.Single(host.Started);
        Assert.Equal(Lan, started.Address);
        Assert.True(started.Https);
        Assert.Equal(editor.CaptionText, started.Package.Caption);
        Assert.Equal(2, started.Package.ImagePaths.Count);
        Assert.All(started.Package.ImagePaths, p => Assert.True(File.Exists(p)));
        Assert.StartsWith(state.Store.Paths.ExportDir(editor.Post.Id), started.Package.ImagePaths[0]);
        Assert.Equal(started.Session.Url, vm.Url);
        Assert.True(vm.QrPng!.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        Assert.Equal(1.0, vm.Progress);
        Assert.False(vm.ShowConnectionHelp);
    }

    [Fact]
    public async Task NoRequestFor60s_ShowsHelp_AndARequestHidesIt()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var time = new FakeTimeProvider();
        var (vm, _, _) = await Create(dir, host, time);
        await vm.StartAsync();

        time.Advance(TimeSpan.FromSeconds(61));
        Assert.True(vm.ShowConnectionHelp);

        host.Started[0].Session.SimulateRequest();
        Assert.True(vm.PhoneConnected);
        Assert.False(vm.ShowConnectionHelp);
    }

    [Fact]
    public async Task ARequestBefore60s_PreventsTheHelp()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var time = new FakeTimeProvider();
        var (vm, _, _) = await Create(dir, host, time);
        await vm.StartAsync();

        host.Started[0].Session.SimulateRequest();
        time.Advance(TimeSpan.FromSeconds(90));

        Assert.False(vm.ShowConnectionHelp);
    }

    [Fact]
    public async Task ChangingTheAddress_RestartsTheSession()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan, Other);
        var (vm, _, _) = await Create(dir, host, new FakeTimeProvider());
        await vm.StartAsync();

        vm.SelectedAddress = Other;
        await vm.Restarting;

        Assert.Equal(2, host.Started.Count);
        Assert.True(host.Started[0].Session.Disposed);
        Assert.Equal(Other, host.Started[1].Address);
        Assert.Equal(host.Started[1].Session.Url, vm.Url);
    }

    [Fact]
    public async Task Close_StopsServer()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var time = new FakeTimeProvider();
        var (vm, _, _) = await Create(dir, host, time);
        await vm.StartAsync();

        await vm.CloseAsync();
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(host.Started[0].Session.Disposed);
        Assert.False(vm.ShowConnectionHelp);
    }

    [Fact]
    public async Task IdleStop_ClearsTheCode()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var (vm, _, _) = await Create(dir, host, new FakeTimeProvider());
        await vm.StartAsync();

        host.Started[0].Session.SimulateIdleStop();

        Assert.Null(vm.Url);
        Assert.Contains("idle", vm.Status);
    }

    [Fact]
    public async Task NoNetwork_ExplainsInsteadOfStarting()
    {
        using var dir = new TempDir();
        var host = new FakeHost();
        var (vm, _, _) = await Create(dir, host, new FakeTimeProvider());

        await vm.StartAsync();

        Assert.Empty(host.Started);
        Assert.Contains("No network connection", vm.Status);
    }

    [Fact]
    public async Task ExportFailure_IsReported()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var (vm, _, _) = await Create(dir, host, new FakeTimeProvider(), realSource: false);

        await vm.StartAsync();

        Assert.Empty(host.Started);
        Assert.True(vm.HasError);
        Assert.Contains("not found", vm.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportToFolder_CreatesATitledSubfolder()
    {
        using var dir = new TempDir();
        var host = new FakeHost(Lan);
        var (vm, _, _) = await Create(dir, host, new FakeTimeProvider());
        var target = dir.File("out");
        Directory.CreateDirectory(target);

        var folder = await vm.ExportToFolderAsync(target);

        Assert.Equal("Heart Nebula", Path.GetFileName(folder));
        Assert.True(File.Exists(Path.Combine(folder!, "01.jpg")));
        Assert.True(File.Exists(Path.Combine(folder!, "caption.txt")));
        Assert.Empty(host.Started);
    }
}

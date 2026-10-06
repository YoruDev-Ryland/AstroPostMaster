using System.Net;
using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Handoff;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace AstroPostMaster.App.Tests;

/// <summary>Regression tests for the final app review.</summary>
public class ReviewFixTests
{
    private sealed class ThrowingLoader(Exception ex) : IPreviewLoader
    {
        public Task<PreviewResult> LoadAsync(string path, CancellationToken ct = default) => Task.FromException<PreviewResult>(ex);
    }

    private sealed class DisposableImage : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    /// <summary>Hands out disposable images and records the cancellation token of each load.</summary>
    private sealed class TrackingLoader : IPreviewLoader
    {
        public List<DisposableImage> Images { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Task<PreviewResult> LoadAsync(string path, CancellationToken ct = default)
        {
            Tokens.Add(ct);
            var image = new DisposableImage();
            Images.Add(image);
            return Task.FromResult(new PreviewResult(image, 6000, 4000));
        }
    }

    // ---- Critical 1: palette ComboBox writing null ----

    [Fact]
    public void PaletteSelector_CannotEraseAChipGeneratedPalette()
    {
        using var dir = new TempDir();
        var input = new CaptionInput();
        var caption = new CaptionViewModel(input, Sample.State(dir), () => { });
        caption.NarrowbandChips.Single(c => c.Key == "Ha").IsSelected = true;

        caption.Palette = null; // what a ComboBox does when its SelectedItem isn't in ItemsSource

        Assert.Equal("H", input.Palette);
        Assert.Equal("H", caption.FilterText);
        Assert.Contains("H", caption.PaletteOptions);
    }

    // ---- Important 2: relink from the UI ----

    [Fact]
    public async Task OpeningAnImage_WhenTheSourceIsMissing_RelinksTheCurrentPost()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = new Post { Title = "M31", SourcePath = dir.File("moved.jpg"), Slides = [new Slide(SlideKind.Crop, new RectF(0.4, 0.3, 0.1, 0.25)), Slide.FullImage] };
        state.Store.SavePost(post);
        var vm = new MainWindowViewModel(state, new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        Assert.True(vm.Editor!.SourceMissing);

        await vm.OpenImageAsync(Sample.ImageFile(dir, "found.jpg"));

        Assert.Single(vm.Posts);
        Assert.Equal(post.Id, vm.Editor!.Post.Id);
        Assert.False(vm.Editor.SourceMissing);
        Assert.Equal(2, vm.Editor.Slides.Count);
    }

    // ---- Important 3: persistence failures don't crash ----

    [Fact]
    public async Task SaveFailure_IsReportedAndRetriedInsteadOfThrowing()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = new Post { Title = "x", Slides = [Slide.FullImage] };
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        // Make the post folder path a file so writing post.json fails.
        Directory.CreateDirectory(state.Store.Paths.PostsDir);
        File.WriteAllText(state.Store.Paths.PostDir(post.Id), "blocker");

        editor.Caption.Description = "edit";

        Assert.NotNull(editor.SaveError);
        File.Delete(state.Store.Paths.PostDir(post.Id));
        await editor.FlushAsync();
        Assert.Null(editor.SaveError);
        Assert.True(File.Exists(state.Store.Paths.PostFile(post.Id)));
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task DeleteFailure_IsReportedAndThePostStays()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "uses Unix permissions to force the failure");
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var vm = new MainWindowViewModel(state, new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        vm.NewPostCommand.Execute(null);
        var postsDir = state.Store.Paths.PostsDir;
        File.SetUnixFileMode(postsDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            vm.DeletePostCommand.Execute(null);

            Assert.Single(vm.Posts);
            Assert.Contains("delete", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.SetUnixFileMode(postsDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // ---- Important 4: other IO errors while loading the preview ----

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task PreviewIoErrors_SetLoadErrorInsteadOfThrowing(Type exceptionType)
    {
        using var dir = new TempDir();
        var post = new Post { SourcePath = Sample.ImageFile(dir), Slides = [Slide.FullImage] };
        var ex = (Exception)Activator.CreateInstance(exceptionType, "file is locked by another program")!;
        var editor = new EditorViewModel(post, Sample.State(dir), new ThrowingLoader(ex), new ImmediateDispatcher());

        await editor.LoadSourceAsync();

        Assert.Contains("locked", editor.LoadError);
    }

    // ---- Important 5: filmstrip drop index ----

    [Theory]
    [InlineData(0, 0, 0)] // dropped before itself
    [InlineData(0, 1, 0)] // dropped just after itself: no move
    [InlineData(0, 2, 1)] // past B's centre: [B, A, C]
    [InlineData(0, 3, 2)] // past the end
    [InlineData(2, 0, 0)] // leftward moves use the slot directly
    [InlineData(2, 1, 1)]
    public void DropIndex_AccountsForTheRemovedItem(int from, int slot, int expected) =>
        Assert.Equal(expected, EditorViewModel.DropIndex(from, slot));

    // ---- Important 6: aspect changed while the source wasn't loaded ----

    [Fact]
    public async Task AspectChangedWithoutSource_IsAppliedWhenTheSourceLoads()
    {
        using var dir = new TempDir();
        var crop = new Slide(SlideKind.Crop, Core.Slides.SlideGeometry.DefaultCrop(AspectRatio.Portrait4x5, 6000, 4000));
        var post = new Post { SourcePath = Sample.ImageFile(dir), Slides = [crop, Slide.FullImage] };
        var editor = new EditorViewModel(post, Sample.State(dir), new FakePreviewLoader(), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };

        editor.Aspect = AspectRatio.Square;  // source not loaded yet
        await editor.LoadSourceAsync();

        var r = editor.Slides[0].Rect;
        Assert.Equal(1.0, r.W * 6000 / (r.H * 4000), 6);
    }

    // ---- Important 7: overlapping address changes ----

    private sealed class SlowHost(params IPAddress[] addresses) : IHandoffHost
    {
        public List<(TaskCompletionSource<IHandoffSession> Pending, SlowSession Session)> Starts { get; } = [];
        public IReadOnlyList<IPAddress> Addresses() => addresses;
        public Task<IHandoffSession> StartAsync(HandoffPackage package, IPAddress address, bool https, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<IHandoffSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Starts.Add((tcs, new SlowSession(new Uri($"https://{address}:48443/t/"))));
            return tcs.Task;
        }
        public void CompleteAll() { foreach (var (p, s) in Starts) p.TrySetResult(s); }
    }

    private sealed class SlowSession(Uri url) : IHandoffSession
    {
        public Uri Url { get; } = url;
        public bool HasReceivedRequest => false;
        public bool Disposed { get; private set; }
        public event Action? RequestReceived { add { } remove { } }
        public event Action? Stopped { add { } remove { } }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task RapidAddressChanges_NeverLeaveAServerRunningAfterClose()
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
        var host = new SlowHost(IPAddress.Parse("192.168.1.2"), IPAddress.Parse("10.0.0.2"), IPAddress.Parse("172.20.0.2"));
        var vm = new PhoneExportViewModel(editor, state, new ImmediateDispatcher(), host, new FakeTimeProvider());

        var start = vm.StartAsync();
        while (host.Starts.Count == 0) await Task.Delay(10);
        host.CompleteAll();
        await start;
        vm.SelectedAddress = IPAddress.Parse("10.0.0.2");
        vm.SelectedAddress = IPAddress.Parse("172.20.0.2");
        for (var i = 0; i < 50 && host.Starts.Count < 3; i++) { host.CompleteAll(); await Task.Delay(10); }
        host.CompleteAll();
        await vm.Restarting;
        await vm.CloseAsync();
        host.CompleteAll();
        await Task.Delay(50);

        Assert.All(host.Starts, s => Assert.True(s.Session.Disposed, $"session {s.Session.Url} still running"));
    }

    // ---- Important 8: preview memory and cancellation on post switch ----

    [Fact]
    public async Task SwitchingPosts_DisposesThePreviousPreview_AndCancelsItsLoad()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        state.Store.SavePost(new Post { Title = "a", SourcePath = Sample.ImageFile(dir, "a.jpg"), Slides = [Slide.FullImage] });
        Thread.Sleep(20);
        state.Store.SavePost(new Post { Title = "b", SourcePath = Sample.ImageFile(dir, "b.jpg"), Slides = [Slide.FullImage] });
        var loader = new TrackingLoader();
        var vm = new MainWindowViewModel(state, loader, new ImmediateDispatcher());
        await vm.InitializeAsync();

        vm.SelectedPost = vm.Posts[1];
        await vm.EditorLoading;

        Assert.True(loader.Images[0].Disposed);
        Assert.False(loader.Images[1].Disposed);
        Assert.True(loader.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task DeletingAPostWhileItsImageLoads_DoesNotResurrectIt()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var gate = new TaskCompletionSource<PreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new MainWindowViewModel(state, new GatedLoader(gate.Task), new ImmediateDispatcher());
        await vm.InitializeAsync();
        var open = vm.OpenImageAsync(Sample.ImageFile(dir));
        var id = vm.Editor!.Post.Id;

        vm.DeletePostCommand.Execute(null);
        gate.SetResult(new PreviewResult(null, 6000, 4000));
        await open;
        await Task.Delay(700);

        Assert.False(Directory.Exists(state.Store.Paths.PostDir(id)));
    }

    private sealed class GatedLoader(Task<PreviewResult> gate) : IPreviewLoader
    {
        public Task<PreviewResult> LoadAsync(string path, CancellationToken ct = default) => gate;
    }
}

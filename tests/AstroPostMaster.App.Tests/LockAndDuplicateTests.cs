using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.App.Tests;

public class LockAndDuplicateTests
{
    private static async Task<MainWindowViewModel> WithPost(TempDir dir)
    {
        var vm = new MainWindowViewModel(Sample.State(dir), new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        await vm.OpenImageAsync(Sample.ImageFile(dir));
        vm.Editor!.SaveDelay = TimeSpan.Zero;
        vm.Editor.AddCropCommand.Execute(null);
        return vm;
    }

    private static Post Saved(MainWindowViewModel vm, string id) =>
        JsonFile.LoadOrDefault<Post?>(vm.State.Store.Paths.PostFile(id), () => null)!;

    [Fact]
    public async Task LockedPost_IgnoresEveryEdit()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        var editor = vm.Editor!;
        var before = System.Text.Json.JsonSerializer.Serialize(editor.Post, Json.Options);

        editor.IsLocked = true;
        var locked = System.Text.Json.JsonSerializer.Serialize(editor.Post, Json.Options);
        editor.AddCropCommand.Execute(null);
        editor.AddPanoramaCommand.Execute(3);
        editor.AddFullImageCommand.Execute(null);
        editor.UpdateFrame(editor.Slides[0], new RectF(0, 0, 0.1, 0.1));
        editor.MoveSlide(0, 1);
        editor.RemoveSelectedSlideCommand.Execute(null);
        editor.Aspect = AspectRatio.Square;
        editor.ExportSize = ExportSize.Instagram1080;
        editor.Title = "changed";
        editor.Caption.Description = "changed";
        editor.Caption.TargetText = "changed";
        editor.Caption.IntegrationText = "1h";
        editor.Caption.BroadbandChips[0].IsSelected = true;
        await editor.ReplaceImageAsync(Sample.ImageFile(dir, "other.jpg"));

        Assert.Equal(locked, System.Text.Json.JsonSerializer.Serialize(editor.Post, Json.Options));
        Assert.Contains("\"isLocked\": true", locked);
        Assert.False(editor.Caption.BroadbandChips[0].IsSelected); // the chip snaps back
        Assert.NotEqual(before, locked);
        Assert.True(Saved(vm, editor.Post.Id).IsLocked);
    }

    [Fact]
    public async Task LockedPost_CanStillBeBrowsed_AndUnlocked()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        var editor = vm.Editor!;
        editor.IsLocked = true;

        editor.SelectedSlide = editor.Slides[1];
        Assert.Same(editor.Slides[1], editor.SelectedSlide);

        editor.IsLocked = false;
        editor.AddCropCommand.Execute(null);
        Assert.Equal(3, editor.Slides.Count);
        Assert.False(Saved(vm, editor.Post.Id).IsLocked);
    }

    [Fact]
    public async Task LockedPost_CannotBeDeleted()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        vm.Editor!.IsLocked = true;

        vm.DeletePostCommand.Execute(null);

        Assert.Single(vm.Posts);
        Assert.Equal("Post is locked.", vm.StatusMessage);
    }

    [Fact]
    public async Task ToggleLock_WorksOnAPostThatIsNotOpen()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        var first = vm.SelectedPost!;
        vm.NewPostCommand.Execute(null);

        vm.ToggleLockCommand.Execute(first);

        Assert.True(first.IsLocked);
        Assert.True(Saved(vm, first.Post.Id).IsLocked);
        Assert.False(vm.Editor!.IsLocked);

        vm.ToggleLockCommand.Execute(vm.SelectedPost);
        Assert.True(vm.Editor.IsLocked);
        Assert.True(vm.SelectedPost!.IsLocked);
    }

    [Fact]
    public async Task Duplicate_OfALockedPost_IsEditable()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        vm.Editor!.IsLocked = true;

        vm.DuplicatePostCommand.Execute(vm.SelectedPost);

        Assert.False(vm.Editor!.IsLocked);
        vm.Editor.AddCropCommand.Execute(null);
        Assert.Equal(3, vm.Editor.Slides.Count);
    }

    [Fact]
    public async Task ReplaceImage_KeepsTheSlides_AndRefitsThemToTheNewImage()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var vm = new MainWindowViewModel(state, new SizeByNameLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        await vm.OpenImageAsync(Sample.ImageFile(dir, "wide.jpg"));       // 6000x4000
        var editor = vm.Editor!;
        editor.AddCropCommand.Execute(null);
        editor.AddPanoramaCommand.Execute(2);
        var title = editor.Title;

        await editor.ReplaceImageAsync(Sample.ImageFile(dir, "tall.jpg")); // 3000x4500

        Assert.Equal(Path.Combine(dir.Path, "tall.jpg"), editor.Post.SourcePath);
        Assert.Equal((3000, 4500), (editor.SourceWidth, editor.SourceHeight));
        Assert.Equal(3, editor.Slides.Count);
        Assert.Equal(title, editor.Title);
        foreach (var slide in editor.Slides.Where(s => !s.IsFull))
        {
            Assert.InRange(slide.Rect.Right, 0, 1 + 1e-9);
            Assert.InRange(slide.Rect.Bottom, 0, 1 + 1e-9);
            Assert.Equal(0.8 * slide.Panels, slide.Rect.W * 3000 / (slide.Rect.H * 4500), 6);
        }
    }

    [Fact]
    public async Task OpeningAnImage_RelinksAMissingSource_EvenWhenLocked()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = new Post { Title = "M31", SourcePath = dir.File("moved.jpg"), IsLocked = true, Slides = [Slide.FullImage] };
        state.Store.SavePost(post);
        var vm = new MainWindowViewModel(state, new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();

        await vm.OpenImageAsync(Sample.ImageFile(dir, "found.jpg"));

        Assert.Single(vm.Posts);
        Assert.False(vm.Editor!.SourceMissing);
        Assert.True(vm.Editor.IsLocked);
    }

    [Fact]
    public async Task ChoosingAnExportSize_BecomesTheDefaultForNewPosts()
    {
        using var dir = new TempDir();
        var vm = await WithPost(dir);
        Assert.Equal(ExportSize.Full, vm.Editor!.ExportSize);

        vm.Editor.ExportSize = ExportSize.Instagram1080;
        vm.NewPostCommand.Execute(null);

        Assert.Equal(ExportSize.Instagram1080, vm.Editor!.ExportSize);
        Assert.Equal(ExportSize.Instagram1080, AstroPostMaster.App.Services.AppState.Load(vm.State.Store).Settings.ExportSize);
    }

    /// <summary>Dimensions from the file name: "tall" is portrait, anything else 6000x4000.</summary>
    private sealed class SizeByNameLoader : AstroPostMaster.App.Services.IPreviewLoader
    {
        public Task<AstroPostMaster.App.Services.PreviewResult> LoadAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(Path.GetFileName(path).StartsWith("tall")
                ? new AstroPostMaster.App.Services.PreviewResult(null, 3000, 4500)
                : new AstroPostMaster.App.Services.PreviewResult(null, 6000, 4000));
    }
}

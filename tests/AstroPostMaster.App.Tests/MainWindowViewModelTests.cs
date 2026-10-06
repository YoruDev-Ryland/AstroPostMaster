using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.App.Tests;

public class MainWindowViewModelTests
{
    private static async Task<MainWindowViewModel> Create(TempDir dir, bool withProfiles = true)
    {
        var vm = new MainWindowViewModel(Sample.State(dir, withProfiles), new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        return vm;
    }

    [Fact]
    public async Task Initialize_LoadsPostsNewestFirst_AndSelectsTheFirst()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        state.Store.SavePost(new Post { Title = "older" });
        Thread.Sleep(20);
        state.Store.SavePost(new Post { Title = "newer" });

        var vm = new MainWindowViewModel(state, new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();

        Assert.Equal(new[] { "newer", "older" }, vm.Posts.Select(p => p.Title));
        Assert.Same(vm.Posts[0], vm.SelectedPost);
        Assert.Equal("newer", vm.Editor!.Post.Title);
    }

    [Fact]
    public async Task NewPost_AppliesProfileDefaults_AndSelectsIt()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);

        vm.NewPostCommand.Execute(null);

        Assert.Single(vm.Posts);
        Assert.Same(vm.Posts[0], vm.SelectedPost);
        var post = vm.Editor!.Post;
        Assert.Equal("s1", post.Caption.SiteId);
        Assert.Equal(new[] { "h1" }, post.Caption.HashtagSetIds);
        Assert.Equal(new[] { Slide.FullImage }, post.Slides);
        Assert.True(File.Exists(state(vm).Store.Paths.PostFile(post.Id)));
    }

    [Fact]
    public async Task OpenImage_OnFreshApp_CreatesTitledPost_AndGuessesTheTarget()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);
        var image = Sample.ImageFile(dir, "Heart Nebula.jpg");

        await vm.OpenImageAsync(image);

        var editor = vm.Editor!;
        Assert.Equal("Heart Nebula", editor.Post.Title);
        Assert.Equal(image, editor.Post.SourcePath);
        Assert.Equal((6000, 4000), (editor.SourceWidth, editor.SourceHeight));
        Assert.Equal("Heart Nebula (IC 1805)", editor.Caption.TargetText);
        Assert.Contains("#heartnebula", editor.CaptionText);
    }

    [Fact]
    public async Task OpenImage_WhenCurrentPostHasNoSource_UsesIt()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);
        vm.NewPostCommand.Execute(null);
        var id = vm.Editor!.Post.Id;

        await vm.OpenImageAsync(Sample.ImageFile(dir));

        Assert.Single(vm.Posts);
        Assert.Equal(id, vm.Editor!.Post.Id);
    }

    [Fact]
    public async Task OpenImage_WhenCurrentPostHasASource_StartsANewPost()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);
        await vm.OpenImageAsync(Sample.ImageFile(dir, "a.jpg"));

        await vm.OpenImageAsync(Sample.ImageFile(dir, "b.jpg"));

        Assert.Equal(2, vm.Posts.Count);
        Assert.Equal("b", vm.Editor!.Post.Title);
    }

    [Fact]
    public async Task OpenImage_MissingFile_ReportsStatusInsteadOfThrowing()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);

        await vm.OpenImageAsync(dir.File("nope.jpg"));

        Assert.Contains("nope.jpg", vm.StatusMessage);
    }

    [Fact]
    public async Task DuplicatePost_CopiesCaptionButNotSlidesOrSource()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);
        await vm.OpenImageAsync(Sample.ImageFile(dir));
        vm.Editor!.Caption.Description = "First light";

        vm.DuplicatePostCommand.Execute(null);

        Assert.Equal(2, vm.Posts.Count);
        Assert.Equal("Heart Nebula copy", vm.Editor!.Post.Title);
        Assert.Null(vm.Editor.Post.SourcePath);
        Assert.Equal("First light", vm.Editor.Post.Caption.Description);
    }

    [Fact]
    public async Task DeletePost_RemovesFromListAndDisk_AndSelectsNext()
    {
        using var dir = new TempDir();
        var vm = await Create(dir);
        vm.NewPostCommand.Execute(null);
        vm.NewPostCommand.Execute(null);
        var doomed = vm.Editor!.Post.Id;

        vm.DeletePostCommand.Execute(null);

        Assert.Single(vm.Posts);
        Assert.False(Directory.Exists(state(vm).Store.Paths.PostDir(doomed)));
        Assert.Same(vm.Posts[0], vm.SelectedPost);
    }

    [Fact]
    public async Task NewPost_WithNoProfiles_StillBuildsCaption()
    {
        using var dir = new TempDir();
        var vm = await Create(dir, withProfiles: false);

        Assert.True(vm.NeedsProfileSetup);
        await vm.OpenImageAsync(Sample.ImageFile(dir, "M31.jpg"));

        Assert.Null(vm.Editor!.Post.Caption.SiteId);
        Assert.StartsWith("Andromeda Galaxy (M31)", vm.Editor.CaptionText);
    }

    private static AstroPostMaster.App.Services.AppState state(MainWindowViewModel vm) => vm.State;
}

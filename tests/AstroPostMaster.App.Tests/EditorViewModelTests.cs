using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.App.Tests;

public class EditorViewModelTests
{
    private static async Task<EditorViewModel> Editor(TempDir dir, int w = 6000, int h = 4000)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        state.Store.SavePost(post);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(w, h), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(Sample.ImageFile(dir));
        return editor;
    }

    private static double PixelRatio(RectF r, int w, int h) => r.W * w / (r.H * h);

    [Fact]
    public async Task AddCrop_AddsCentredDefaultCropBeforeTheFullSlide_AndSelectsIt()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);

        editor.AddCropCommand.Execute(null);

        Assert.Equal(2, editor.Slides.Count);
        var crop = editor.Slides[0];
        Assert.Equal(SlideKind.Crop, crop.Kind);
        Assert.Equal(SlideGeometry.DefaultCrop(AspectRatio.Portrait4x5, 6000, 4000), crop.Rect);
        Assert.Same(crop, editor.SelectedSlide);
        Assert.True(crop.IsSelected);
        Assert.Equal(SlideKind.Full, editor.Slides[^1].Kind);
        Assert.Equal(SlideKind.Crop, editor.Post.Slides[0].Kind);
    }

    [Fact]
    public async Task AddPanorama_UsesDefaultBand_AndLabelsPanels()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);

        editor.AddPanoramaCommand.Execute(3);

        var pano = editor.Slides[1];
        Assert.Equal(3, pano.Panels);
        Assert.Equal(SlideGeometry.DefaultPanorama(AspectRatio.Portrait4x5, 3, 6000, 4000), pano.Rect);
        Assert.Equal(new[] { "1", "2–4", "5" }, editor.Slides.Select(s => s.Label));
        Assert.Equal(3, pano.PanelRects.Count);
        Assert.Equal(5, editor.SlideCount);
    }

    [Fact]
    public async Task ChangingAspect_RefitsAllFrames()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        editor.AddPanoramaCommand.Execute(4);

        editor.Aspect = AspectRatio.Square;

        Assert.Equal(AspectRatio.Square, editor.Post.Aspect);
        foreach (var slide in editor.Slides.Where(s => s.Kind != SlideKind.Full))
        {
            Assert.Equal(1.0 * slide.Panels, PixelRatio(slide.Rect, 6000, 4000), 6);
            Assert.InRange(slide.Rect.X, -1e-9, 1);
            Assert.InRange(slide.Rect.Right, 0, 1 + 1e-9);
            Assert.InRange(slide.Rect.Bottom, 0, 1 + 1e-9);
        }
    }

    [Fact]
    public async Task UpdateFrame_ClampsOffEdgeProposals()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        var crop = editor.Slides[0];

        editor.UpdateFrame(crop, new RectF(0.95, 0.9, 0.2, 0.3));

        Assert.InRange(crop.Rect.Right, 0, 1 + 1e-9);
        Assert.InRange(crop.Rect.Bottom, 0, 1 + 1e-9);
        Assert.Equal(0.8, PixelRatio(crop.Rect, 6000, 4000), 6);
        Assert.Equal(crop.Rect, editor.Post.Slides[0].Rect);
    }

    [Fact]
    public async Task MoveSlide_ReordersModelAndViewModels_AndKeepsSelection()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        var crop = editor.Slides[0];

        editor.MoveSlide(0, 1);

        Assert.Equal(SlideKind.Full, editor.Slides[0].Kind);
        Assert.Same(crop, editor.Slides[1]);
        Assert.Equal(SlideKind.Crop, editor.Post.Slides[1].Kind);
        Assert.Same(crop, editor.SelectedSlide);
        Assert.Equal(new[] { "1", "2" }, editor.Slides.Select(s => s.Label));
    }

    [Fact]
    public async Task RemoveSelectedSlide_SelectsANeighbour()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);

        editor.RemoveSelectedSlideCommand.Execute(null);

        Assert.Single(editor.Slides);
        Assert.Same(editor.Slides[0], editor.SelectedSlide);
        Assert.Single(editor.Post.Slides);
    }

    [Fact]
    public async Task Warnings_RefreshAfterATinyCrop()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.ExportSize = ExportSize.Instagram1080; // full resolution never upscales, so pin a fixed size
        editor.AddCropCommand.Execute(null);

        editor.UpdateFrame(editor.Slides[0], new RectF(0.4, 0.4, 0.05, 0.1));

        Assert.Contains(editor.Warnings, w => w.Kind == WarningKind.LowResolution && w.SlideNumber == 1);
    }

    [Fact]
    public async Task CountsReflectTheCaption()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);

        Assert.Equal(Core.Captions.CaptionStats.CharacterCount(editor.CaptionText), editor.CharacterCount);
        Assert.Equal(4, editor.HashtagCount); // #heartnebula #ic1805 #astrophotography #astro
        Assert.False(editor.TooManyHashtags);
    }

    [Fact]
    public async Task MissingSource_ShowsWarningAndRelinkRestores()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = new Post { Title = "M31", SourcePath = dir.File("moved-away.jpg"), Slides = [Slide.FullImage] };
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(), new ImmediateDispatcher());

        await editor.LoadSourceAsync();

        Assert.True(editor.SourceMissing);
        Assert.Contains(editor.Warnings, w => w.Kind == WarningKind.SourceMissing);

        await editor.SetSourceAsync(Sample.ImageFile(dir, "found.jpg"));

        Assert.False(editor.SourceMissing);
        Assert.DoesNotContain(editor.Warnings, w => w.Kind == WarningKind.SourceMissing);
    }

    [Fact]
    public async Task Edits_AreDebouncedAndLastEditIsSaved()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.SaveDelay = TimeSpan.FromMilliseconds(80);
        await editor.FlushAsync();
        var before = editor.SaveCount;

        for (var i = 0; i < 10; i++) editor.Caption.Description = $"edit {i}";
        await Task.Delay(400);

        Assert.Equal(before + 1, editor.SaveCount);
        var saved = JsonFile.LoadOrDefault<Post?>(Sample.State(dir).Store.Paths.PostFile(editor.Post.Id), () => null)!;
        Assert.Equal("edit 9", saved.Caption.Description);
    }

    [Fact]
    public async Task FlushAsync_SavesPendingEditImmediately()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.SaveDelay = TimeSpan.FromSeconds(30);

        editor.Caption.Description = "pending";
        await editor.FlushAsync();

        var saved = JsonFile.LoadOrDefault<Post?>(Sample.State(dir).Store.Paths.PostFile(editor.Post.Id), () => null)!;
        Assert.Equal("pending", saved.Caption.Description);
    }
}

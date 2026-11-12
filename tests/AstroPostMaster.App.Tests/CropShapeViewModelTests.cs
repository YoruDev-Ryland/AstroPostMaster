using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.App.Tests;

public class CropShapeViewModelTests
{
    private static async Task<EditorViewModel> Editor(TempDir dir)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(6000, 4000), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(Sample.ImageFile(dir));
        return editor;
    }

    private static double PixelRatio(SlideViewModel s) => s.Rect.W * 6000 / (s.Rect.H * 4000);

    [Fact]
    public async Task Custom_StartsFromTheCurrentShape_AndFollowsTypedRatio()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.Aspect = AspectRatio.Landscape16x9;
        editor.AddCropCommand.Execute(null);

        editor.Aspect = AspectRatio.Custom;
        Assert.Equal("16:9", editor.CustomAspectText);
        Assert.Equal(16.0 / 9, PixelRatio(editor.Slides[0]), 6);

        editor.CustomAspectText = "5:4";
        Assert.Equal(1.25, PixelRatio(editor.Slides[0]), 6);
        Assert.Equal("5:4", editor.AspectLabel);
        Assert.Equal(5, editor.Post.CustomAspectWidth);

        editor.CustomAspectText = "nonsense";
        Assert.Equal("5:4", editor.CustomAspectText);
    }

    [Fact]
    public async Task Free_KeepsWhateverShapeIsDragged()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        editor.Aspect = AspectRatio.Free;

        var slide = editor.Slides[0];
        editor.UpdateFrame(slide, new RectF(0.1, 0.1, 0.6, 0.2)); // 3600×800

        Assert.Equal(new RectF(0.1, 0.1, 0.6, 0.2), slide.Rect);
        Assert.Equal(4.5, editor.SlideRatio(slide), 6);
        Assert.Equal("Free", editor.AspectLabel);

        editor.AddCropCommand.Execute(null); // copies the selected frame's shape
        Assert.Equal(4.5, PixelRatio(editor.Slides[1]), 6);
    }

    [Fact]
    public async Task RotateFrame_StaysInside_AndTurningBackRestoresTheSize()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddPanoramaCommand.Execute(3);
        var slide = editor.Slides[0];
        var start = slide.Rect;

        editor.RotateFrame(slide, 30, start);
        Assert.Equal(30, slide.Angle);
        Assert.True(slide.Rect.W < start.W);
        foreach (var (x, y) in SlideGeometry.Corners(slide.Rect, 30, 6000, 4000))
        {
            Assert.InRange(x, -1e-6, 6000 + 1e-6);
            Assert.InRange(y, -1e-6, 4000 + 1e-6);
        }

        editor.RotateFrame(slide, 0, start);
        Assert.Equal(start.W, slide.Rect.W, 9);
        Assert.Equal(0, slide.Angle);
    }

    [Fact]
    public async Task SelectedAngle_RotatesTheSelectedFrame_AndIgnoresTheFullImage()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);

        editor.SelectedAngle = -12.5m;
        Assert.Equal(-12.5, editor.SelectedSlide!.Angle);
        Assert.True(editor.CanRotate);

        editor.AddFullImageCommand.Execute(null);
        editor.SelectedSlide = editor.Slides.Single(s => s.IsFull);
        Assert.Null(editor.SelectedAngle);
        Assert.False(editor.CanRotate);
    }

    [Fact]
    public async Task Locked_IgnoresShapeAndRotationChanges()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        editor.IsLocked = true;

        editor.Aspect = AspectRatio.Free;
        editor.SelectedAngle = 10;

        Assert.Equal(AspectRatio.Portrait4x5, editor.Aspect);
        Assert.All(editor.Slides, s => Assert.Equal(0, s.Angle));
    }
}

using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.App.Tests;

public class AspectAndExportSizeViewModelTests
{
    private static async Task<(EditorViewModel Editor, AppState State)> Editor(TempDir dir)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(6000, 4000), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(Sample.ImageFile(dir));
        return (editor, state);
    }

    [Fact]
    public async Task LandscapeAspect_GivesLandscapeFramesAndThumbnails()
    {
        using var dir = new TempDir();
        var (editor, _) = await Editor(dir);
        editor.AspectIndex = 6; // 16:9

        editor.AddCropCommand.Execute(null);

        var crop = editor.Slides[0];
        Assert.Equal(AspectRatio.Landscape16x9, editor.Aspect);
        Assert.Equal(16.0 / 9, crop.Rect.W * 6000 / (crop.Rect.H * 4000), 6);
        Assert.Equal(SlideViewModel.ThumbHeight * 16 / 9, crop.ThumbWidth, 6);
        Assert.Equal("16:9", editor.AspectLabel);
    }

    [Fact]
    public async Task ExportSize_FollowsTheSettingUntilOverridden()
    {
        using var dir = new TempDir();
        var (editor, state) = await Editor(dir);
        Assert.Equal(ExportSize.Full, editor.ExportSize);
        Assert.Null(editor.Post.ExportSize);

        state.Settings.ExportSize = ExportSize.LongEdge2048;
        editor.SettingsChanged();
        Assert.Equal(ExportSize.LongEdge2048, editor.ExportSize);

        editor.ExportSizeIndex = EditorViewModel.ExportSizeOptions.ToList().FindIndex(o => o.Size == ExportSize.Instagram1080);

        Assert.Equal(ExportSize.Instagram1080, editor.ExportSize);
        Assert.Equal(ExportSize.Instagram1080, editor.Post.ExportSize);
        Assert.Equal("1080 px", editor.ExportSizeLabel);
    }

    [Fact]
    public async Task ChangingExportSize_RevalidatesUpscalingWarnings()
    {
        using var dir = new TempDir();
        var (editor, _) = await Editor(dir);
        editor.AddCropCommand.Execute(null);
        editor.UpdateFrame(editor.Slides[0], new RectF(0.45, 0.45, 0.05, 0.1));
        Assert.DoesNotContain(editor.Warnings, w => w.Kind == WarningKind.LowResolution); // full resolution never upscales

        editor.ExportSize = ExportSize.Instagram1080;

        Assert.Contains(editor.Warnings, w => w.Kind == WarningKind.LowResolution);
    }

    [Fact]
    public void Settings_SavesTheDefaultExportSize()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var vm = new SettingsViewModel(state) { ExportSize = ExportSize.LongEdge4096 };

        vm.Save();

        Assert.Equal(ExportSize.LongEdge4096, AppState.Load(state.Store).Settings.ExportSize);
        Assert.Equal(4, SettingsViewModel.ExportSizeOptions.Count);
    }
}

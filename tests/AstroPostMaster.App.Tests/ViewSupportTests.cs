using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.App.Tests;

/// <summary>View-facing helpers on the view models (selector indices, thumbnail sizes, suggestions, redraw signal).</summary>
public class ViewSupportTests
{
    private static async Task<EditorViewModel> Editor(TempDir dir)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(6000, 4000), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(Sample.ImageFile(dir));
        return editor;
    }

    [Fact]
    public async Task AspectIndex_MapsToAspectRatio()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        Assert.Equal(0, editor.AspectIndex);

        editor.AspectIndex = 3;

        Assert.Equal(AspectRatio.Square, editor.Aspect);
        Assert.Equal(new[] { "4:5", "3:4", "2:3", "1:1", "4:3", "3:2", "16:9", "1.91:1" }, EditorViewModel.AspectOptions.Select(o => o.Label));
        Assert.Equal(new[] { "Portrait", "Portrait", "Portrait", "Square", "Landscape", "Landscape", "Landscape", "Landscape" },
            EditorViewModel.AspectOptions.Select(o => o.Group));
    }

    [Fact]
    public async Task ThumbWidth_FollowsAspectAndPanels()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        editor.AddPanoramaCommand.Execute(3);

        Assert.Equal(SlideViewModel.ThumbHeight * 0.8 * 3, editor.Slides[0].ThumbWidth, 6);
        Assert.Equal(SlideViewModel.ThumbHeight * 0.8, editor.Slides[1].ThumbWidth, 6);

        editor.Aspect = AspectRatio.Square;
        Assert.Equal(SlideViewModel.ThumbHeight, editor.Slides[1].ThumbWidth, 6);
    }

    [Fact]
    public async Task Suggest_ReturnsDisplayStringsThatMatchTheSelectedTarget()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);

        var suggestion = editor.Caption.Suggest("thors")[0];
        editor.Caption.TargetText = suggestion.ToString();   // what AutoCompleteBox writes first
        editor.Caption.SelectTarget(suggestion.Entry);         // then the selection handler

        Assert.Equal("Thor's Helmet (NGC 2359)", suggestion.Display);
        Assert.Equal(suggestion.Display, editor.Caption.TargetText);
        Assert.Equal("NGC 2359", editor.Post.Caption.TargetCatalogId);
    }

    [Fact]
    public async Task FramesChanged_IsRaisedForSlideEditsAndSelection()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        var raised = 0;
        editor.FramesChanged += () => raised++;

        editor.AddCropCommand.Execute(null);
        editor.UpdateFrame(editor.Slides[0], new Core.Model.RectF(0.1, 0.1, 0.2, 0.2));
        editor.SelectedSlide = editor.Slides[1];

        Assert.True(raised >= 3);
    }
}

public class StatusLineTests
{
    [Fact]
    public async Task StatusLine_ShowsStatusMessageElseFirstWarning()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        state.Settings.ExportSize = ExportSize.Instagram1080; // an 800x600 source is upscaled only at fixed sizes
        var vm = new MainWindowViewModel(state, new FakePreviewLoader(800, 600), new ImmediateDispatcher());
        await vm.InitializeAsync();
        await vm.OpenImageAsync(Sample.ImageFile(dir));

        Assert.Contains("upscaled", vm.StatusLine);   // 800x600 full slide warning

        vm.StatusMessage = "Saved to /tmp/x";
        Assert.Equal("Saved to /tmp/x", vm.StatusLine);
    }

    [Fact]
    public async Task SettingsChanged_RebuildsThePreviewWithTheNewTemplate()
    {
        using var dir = new TempDir();
        var vm = new MainWindowViewModel(Sample.State(dir), new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        await vm.OpenImageAsync(Sample.ImageFile(dir));

        vm.State.Settings.CaptionTemplate = "{target.display} only";
        vm.SettingsChanged();

        Assert.Equal("Heart Nebula (IC 1805) only", vm.Editor!.CaptionText);
    }
}

public class AutoTargetTests
{
    private static CaptionViewModel Caption(TempDir dir) =>
        new(new CaptionInput(), Sample.State(dir), () => { });

    [Theory]
    [InlineData("M31", "Andromeda Galaxy (M31)")]
    [InlineData("Andromeda", "Andromeda Galaxy (M31)")]
    [InlineData("Heart Nebula-1102250103", "Heart Nebula (IC 1805)")]
    [InlineData("LagoonNebula2", "Lagoon Nebula (M8)")]
    [InlineData("Crescent Nebula_Square", "Crescent Nebula (NGC 6888)")]
    [InlineData("Thor's Helmet", "Thor's Helmet (NGC 2359)")]
    public void TryAutoTarget_RecognisesCommonFileNames(string stem, string expected)
    {
        using var dir = new TempDir();
        var caption = Caption(dir);
        caption.TryAutoTarget(stem);
        Assert.Equal(expected, caption.TargetText);
    }

    [Theory]
    [InlineData("LDN1100")]
    [InlineData("IMG_4512")]
    [InlineData("final")]
    public void TryAutoTarget_LeavesUnknownNamesAlone(string stem)
    {
        using var dir = new TempDir();
        var caption = Caption(dir);
        caption.TryAutoTarget(stem);
        Assert.Equal("", caption.TargetText);
    }

    [Fact]
    public void PostSubtitle_UsesSingularForOneSlide()
    {
        var item = new PostListItemViewModel(new Post { Slides = [Slide.FullImage] });
        Assert.EndsWith("1 slide", item.Subtitle);
    }
}

public class ListAndPlaceholderTests
{
    [Fact]
    public async Task PostList_UpdatesTitleAndSlideCountImmediately()
    {
        using var dir = new TempDir();
        var vm = new MainWindowViewModel(Sample.State(dir), new FakePreviewLoader(), new ImmediateDispatcher());
        await vm.InitializeAsync();
        await vm.OpenImageAsync(Sample.ImageFile(dir, "a.jpg"));
        var item = vm.SelectedPost!;
        var changes = new List<string?>();
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        vm.Editor!.SaveDelay = TimeSpan.FromMinutes(1); // no save, so only the live update can refresh it

        vm.Editor.Title = "Heart Nebula";
        vm.Editor.AddCropCommand.Execute(null);

        Assert.Equal("Heart Nebula", item.Title);
        Assert.EndsWith("2 slides", item.Subtitle);
        Assert.Contains(nameof(PostListItemViewModel.Title), changes);
    }

    [Fact]
    public void IntegrationPlaceholder_ShowsTheTotalOfPerFilterRows()
    {
        using var dir = new TempDir();
        var caption = new CaptionViewModel(new CaptionInput(), Sample.State(dir), () => { });
        Assert.Equal("12h30m", caption.IntegrationPlaceholder);

        var row = caption.AddIntegrationRow();
        row.Subs = 60;
        row.SubSeconds = 300;

        Assert.Equal("5h", caption.IntegrationPlaceholder);
    }
}

using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using Microsoft.Extensions.Time.Testing;

namespace AstroPostMaster.App.Tests;

public class SlideshowViewModelTests
{
    private static async Task<EditorViewModel> Editor(TempDir dir)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(6000, 4000), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(Sample.ImageFile(dir));
        editor.AddCropCommand.Execute(null);      // 1
        editor.AddPanoramaCommand.Execute(2);     // 2-3
        return editor;                             // + full image = 4
    }

    [Fact]
    public async Task Panoramas_ExpandIntoOneItemPerPanel()
    {
        using var dir = new TempDir();
        var editor = await Editor(dir);
        var show = new SlideshowViewModel(editor, new ImmediateDispatcher(), new FakeTimeProvider());

        Assert.Equal(4, show.Count);
        Assert.Equal(new[] { SlideKind.Crop, SlideKind.Crop, SlideKind.Crop, SlideKind.Full }, show.Items.Select(i => i.Kind));
        Assert.Equal(editor.Slides[1].PanelRects[1], show.Items[2].Rect);
        Assert.Equal("1 / 4", show.Position);
        Assert.Equal(0.8, show.Ratio, 9);
    }

    [Fact]
    public async Task AdvancesEveryThreeSeconds_AndStopsOnTheLastSlide()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var show = new SlideshowViewModel(await Editor(dir), new ImmediateDispatcher(), time);
        show.Start();

        time.Advance(TimeSpan.FromSeconds(2.9));
        Assert.Equal(0, show.Index);
        time.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(1, show.Index);
        Assert.False(show.LastChangeWasManual);

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(3, show.Index);
        Assert.False(show.IsPlaying);
    }

    [Fact]
    public async Task ArrowKeys_JumpImmediately_AndRestartTheTimer()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var show = new SlideshowViewModel(await Editor(dir), new ImmediateDispatcher(), time);
        show.Start();

        time.Advance(TimeSpan.FromSeconds(2));
        show.Next();
        Assert.Equal(1, show.Index);
        Assert.True(show.LastChangeWasManual);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, show.Index);         // the 3 s restarted at the key press
        time.Advance(TimeSpan.FromSeconds(1.1));
        Assert.Equal(2, show.Index);

        show.Previous();
        show.Previous();
        show.Previous();
        Assert.Equal(0, show.Index);         // stops at the first slide
    }

    [Fact]
    public async Task GoingBackFromTheEnd_ResumesPlaying()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var show = new SlideshowViewModel(await Editor(dir), new ImmediateDispatcher(), time);
        show.Start();
        time.Advance(TimeSpan.FromSeconds(30));

        show.Previous();
        time.Advance(TimeSpan.FromSeconds(3.1));

        Assert.Equal(3, show.Index);
    }

    [Fact]
    public async Task Close_StopsTheTimer_AndRaisesClosed()
    {
        using var dir = new TempDir();
        var time = new FakeTimeProvider();
        var show = new SlideshowViewModel(await Editor(dir), new ImmediateDispatcher(), time);
        var closed = 0;
        show.Closed += () => closed++;
        show.Start();

        show.Close();
        time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, show.Index);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task MainWindow_StartsAndStopsTheSlideshow()
    {
        using var dir = new TempDir();
        var vm = new MainWindowViewModel(Sample.State(dir), new FakePreviewLoader(), new ImmediateDispatcher(), new FakeTimeProvider());
        await vm.InitializeAsync();
        Assert.False(vm.StartSlideshowCommand.CanExecute(null));
        await vm.OpenImageAsync(Sample.ImageFile(dir));

        vm.StartSlideshowCommand.Execute(null);
        Assert.NotNull(vm.Slideshow);

        vm.Slideshow!.Close();
        Assert.Null(vm.Slideshow);
    }
}

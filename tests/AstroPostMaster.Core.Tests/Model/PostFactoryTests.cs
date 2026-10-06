using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Tests.Model;

public class PostFactoryTests
{
    private static readonly Site[] Sites = [new("s1", "Backyard", 6), new("s2", "SFRO", 1, IsDefault: true)];
    private static readonly Rig[] Rigs = [new("r1", "Starlux")];
    private static readonly SoftwareSet[] Software = [new("w1", "A"), new("w2", "B", IsDefault: true)];
    private static readonly HashtagSet[] Sets = [new("h1", "Core", IsDefault: true), new("h2", "Galaxy"), new("h3", "Nebula", IsDefault: true)];

    [Fact]
    public void CreateNew_AppliesDefaults_AndAddsFullImageSlide()
    {
        var post = PostFactory.CreateNew(Sites, Rigs, Software, Sets);

        Assert.Equal("s2", post.Caption.SiteId);
        Assert.Equal("r1", post.Caption.RigId);
        Assert.Equal("w2", post.Caption.SoftwareId);
        Assert.Equal(new[] { "h1", "h3" }, post.Caption.HashtagSetIds);
        Assert.Equal(new[] { Slide.FullImage }, post.Slides);
        Assert.Equal(AspectRatio.Portrait4x5, post.Aspect);
    }

    [Fact]
    public void CreateNew_WithNoProfiles_LeavesIdsNull()
    {
        var post = PostFactory.CreateNew([], [], [], []);
        Assert.Null(post.Caption.SiteId);
        Assert.Empty(post.Caption.HashtagSetIds);
    }

    [Fact]
    public void Duplicate_CopiesEverything_ButIsUnlocked()
    {
        var source = PostFactory.CreateNew(Sites, Rigs, Software, Sets);
        source.Title = "M31";
        source.SourcePath = "/x/m31.jpg";
        source.Aspect = AspectRatio.Square;
        source.ExportSize = ExportSize.Instagram1080;
        source.IsLocked = true;
        source.Slides.Insert(0, new Slide(SlideKind.Crop, new RectF(0.1, 0.1, 0.2, 0.2)));
        source.Caption.Broadband.AddRange(["R", "G", "B"]);

        var copy = PostFactory.Duplicate(source);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("M31 copy", copy.Title);
        Assert.Equal("/x/m31.jpg", copy.SourcePath);
        Assert.Equal(source.Slides, copy.Slides);
        Assert.Equal(AspectRatio.Square, copy.Aspect);
        Assert.Equal(ExportSize.Instagram1080, copy.ExportSize);
        Assert.False(copy.IsLocked);
        Assert.Equal(new[] { "R", "G", "B" }, copy.Caption.Broadband);
        copy.Caption.Broadband.Add("L");
        copy.Slides.Clear();
        Assert.Equal(3, source.Caption.Broadband.Count);
        Assert.Equal(2, source.Slides.Count);
    }
}

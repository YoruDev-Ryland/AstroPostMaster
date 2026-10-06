using System.Text.Json;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.Core.Tests.Storage;

public class StorageTests
{
    private static Post SamplePost() => new()
    {
        Title = "Messier 106 & Friends",
        SourcePath = "/data/Astro Photos/Messier 106 & Friends Hα.jpg",
        Aspect = AspectRatio.Portrait3x4,
        Slides = [new Slide(SlideKind.Crop, new RectF(0.1, 0.2, 0.3, 0.4)), new Slide(SlideKind.Panorama, new RectF(0, 0.1, 1, 0.5), 3), Slide.FullImage],
        Caption = new CaptionInput
        {
            TargetText = "Andromeda Galaxy (M 31)",
            TargetCatalogId = "M 31",
            Description = "First light {with} braces",
            SiteId = "s1", RigId = "r1", SoftwareId = "w1",
            HashtagSetIds = ["h1", "h2"],
            ExtraHashtags = ["#extra"],
            Broadband = ["R", "G", "B"],
            Palette = "HOO",
            IntegrationText = "12h30m",
            IntegrationRows = [new IntegrationRow("Hα", 60, 300)],
            Dates = [new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 19)],
        },
    };

    [Fact]
    public void Post_RoundTripsThroughJson_WithEnumsAsStrings()
    {
        using var dir = new TempDir();
        var post = SamplePost();
        var path = dir.File("post.json");

        JsonFile.SaveAtomic(path, post);
        var loaded = JsonFile.LoadOrDefault<Post?>(path, () => null);

        Assert.NotNull(loaded);
        Assert.Equal(JsonSerializer.Serialize(post, Json.Options), JsonSerializer.Serialize(loaded, Json.Options));
        Assert.Contains("\"Portrait3x4\"", File.ReadAllText(path));
        Assert.Equal("/data/Astro Photos/Messier 106 & Friends Hα.jpg", loaded.SourcePath);
    }

    [Fact]
    public void SaveAtomic_CreatesDirectories_AndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a", "b", "settings.json");

        JsonFile.SaveAtomic(path, new AppSettings());

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void LoadOrDefault_MovesCorruptFileToBak_AndReturnsFallback()
    {
        using var dir = new TempDir();
        var path = dir.File("sites.json");
        File.WriteAllText(path, "{ this is not json");
        string? reported = null;

        var sites = JsonFile.LoadOrDefault(path, () => new List<Site>(), p => reported = p);

        Assert.Empty(sites);
        Assert.Equal(path, reported);
        Assert.False(File.Exists(path));
        Assert.Equal("{ this is not json", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void LoadOrDefault_MissingFileReturnsFallbackWithoutReporting()
    {
        using var dir = new TempDir();
        var called = false;
        var settings = JsonFile.LoadOrDefault(dir.File("nope.json"), () => new AppSettings(), _ => called = true);
        Assert.Equal(92, settings.JpegQuality);
        Assert.False(called);
    }

    [Fact]
    public void AppStore_SavesAndLoadsProfilesAndPostsNewestFirst()
    {
        using var dir = new TempDir();
        var store = new AppStore(new AppPaths(dir.Path));
        store.SaveSites([new Site("s1", "SFRO", 1, IsDefault: true)]);
        store.SaveRigs([new Rig("r1", "Starlux", Scope: "SkyWatcher Starlux 190mn", Extra: ["Guider: ASI220mm Mini"])]);
        store.SaveSoftware([new SoftwareSet("w1", "Default", ["Siril - Stacking"])]);
        store.SaveHashtagSets([new HashtagSet("h1", "Core", ["#astrophotography"], IsDefault: true)]);

        var older = new Post { Title = "older" };
        store.SavePost(older);
        Thread.Sleep(20);
        var newer = new Post { Title = "newer" };
        store.SavePost(newer);

        Assert.Equal("SFRO - Bortle 1", store.LoadSites().Single().Describe());
        Assert.Equal(new[] { "Guider: ASI220mm Mini" }, store.LoadRigs().Single().Extra);
        Assert.Equal(new[] { "Siril - Stacking" }, store.LoadSoftware().Single().Lines);
        Assert.True(store.LoadHashtagSets().Single().IsDefault);
        Assert.Equal(new[] { "newer", "older" }, store.LoadPosts().Select(p => p.Title));

        store.DeletePost(newer.Id);
        Assert.Equal(new[] { "older" }, store.LoadPosts().Select(p => p.Title));
    }

    [Fact]
    public void AppStore_SkipsCorruptPost_AndRaisesEvent()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        var store = new AppStore(paths);
        store.SavePost(new Post { Title = "good" });
        Directory.CreateDirectory(paths.PostDir("broken"));
        File.WriteAllText(paths.PostFile("broken"), "garbage");
        var recovered = new List<string>();
        store.CorruptFileRecovered += recovered.Add;

        var posts = store.LoadPosts();

        Assert.Equal(new[] { "good" }, posts.Select(p => p.Title));
        Assert.Equal(new[] { paths.PostFile("broken") }, recovered);
    }

    [Fact]
    public void Profiles_RecordsTolerateMissingOptionalFields()
    {
        var rig = JsonSerializer.Deserialize<Rig>("""{"id":"r","name":"Bare"}""", Json.Options)!;
        Assert.Equal("", rig.Scope);
        Assert.Null(rig.Extra);
        Assert.False(rig.IsDefault);
    }

    [Fact]
    public void LoadPosts_TakesIdFromFolderName_NotFromJson()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        var store = new AppStore(paths);
        var original = new Post { Title = "original" };
        store.SavePost(original);
        var copyDir = Path.Combine(paths.PostsDir, "copy-of-original");
        Directory.CreateDirectory(copyDir);
        File.Copy(paths.PostFile(original.Id), Path.Combine(copyDir, "post.json"));
        Directory.CreateDirectory(paths.PostDir("blank"));
        File.WriteAllText(paths.PostFile("blank"), """{"id":"","title":"blank"}""");

        var posts = store.LoadPosts();

        Assert.Contains(posts, p => p.Id == "copy-of-original");
        Assert.Contains(posts, p => p.Id == "blank");
        Assert.Contains(posts, p => p.Id == original.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("../x")]
    public void DeletePost_RejectsUnsafeIds(string id)
    {
        using var dir = new TempDir();
        var store = new AppStore(new AppPaths(dir.Path));
        store.SavePost(new Post { Title = "keep" });

        Assert.Throws<ArgumentException>(() => store.DeletePost(id));
        Assert.Single(store.LoadPosts());
    }
}

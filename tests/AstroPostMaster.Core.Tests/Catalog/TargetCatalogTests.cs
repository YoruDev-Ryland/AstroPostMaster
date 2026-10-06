using AstroPostMaster.Core.Catalog;

namespace AstroPostMaster.Core.Tests.Catalog;

public class TargetCatalogTests
{
    private static readonly CatalogEntry Andromeda = new(["M31", "NGC 224"], "Andromeda Galaxy", ["Messier 31"], "G");
    private static readonly CatalogEntry Heart = new(["IC 1805", "Sh2-190"], "Heart Nebula", [], "HII");
    private static readonly CatalogEntry Squatter = new(["NGC 9999"], "Squatter", ["IC 1805"], "G");
    private static readonly CatalogEntry Sh86 = new(["Sh2-86"], "", [], "HII");
    private static readonly CatalogEntry Moon = new(["Moon"], "Moon", ["Luna"], "Solar System");

    private static TargetCatalog Catalog(params CatalogEntry[] user) => new([Andromeda, Heart, Squatter, Sh86, Moon], user);

    [Theory]
    [InlineData("M31")]
    [InlineData("m 31")]
    [InlineData("M031")]
    [InlineData("NGC224")]
    [InlineData("ngc 0224")]
    [InlineData("andromeda galaxy")]
    [InlineData("Messier 31")]
    public void Find_IsCaseSpaceAndLeadingZeroInsensitive(string query) =>
        Assert.Same(Andromeda, Catalog().Find(query));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NGC 1")]
    public void Find_ReturnsNullWhenUnknown(string query) => Assert.Null(Catalog().Find(query));

    [Fact]
    public void Find_AliasNeverShadowsAnotherObjectsDesignation() =>
        Assert.Same(Heart, Catalog().Find("IC 1805"));

    [Fact]
    public void UserEntries_ReplaceBundledEntriesSharingAnId()
    {
        var mine = new CatalogEntry(["M31"], "My Andromeda");
        var catalog = Catalog(mine);
        Assert.Same(mine, catalog.Find("m31"));
        Assert.Null(catalog.Find("Andromeda Galaxy"));
    }

    [Fact]
    public void Search_ReturnsExactThenPrefixMatches_UpToMax()
    {
        var catalog = Catalog();
        Assert.Equal(new[] { Andromeda }, catalog.Search("andro"));
        Assert.Same(Heart, catalog.Search("heart nebula")[0]);
        Assert.Single(catalog.Search("n", max: 1));
        Assert.Empty(catalog.Search(" "));
    }

    [Fact]
    public void Display_CombinesNameAndPrimaryId()
    {
        Assert.Equal("Andromeda Galaxy (M31)", TargetCatalog.Display(Andromeda));
        Assert.Equal("Sh2-86", TargetCatalog.Display(Sh86));
        Assert.Equal("Moon", TargetCatalog.Display(Moon));
    }

    [Fact]
    public void DefaultHashtags_UseNameAndPrimaryId_UnlessOverridden()
    {
        Assert.Equal(new[] { "#andromedagalaxy", "#m31" }, TargetCatalog.DefaultHashtags(Andromeda));
        Assert.Equal(new[] { "#sh286" }, TargetCatalog.DefaultHashtags(Sh86));
        var custom = Andromeda with { Hashtags = ["#M31", "#andromeda"] };
        Assert.Equal(new[] { "#m31", "#andromeda" }, TargetCatalog.DefaultHashtags(custom));
    }

    [Fact]
    public void AppStore_RoundTripsUserCatalog()
    {
        using var dir = new TempDir();
        var store = new AstroPostMaster.Core.Storage.AppStore(new AstroPostMaster.Core.Storage.AppPaths(dir.Path));
        store.SaveUserCatalog([new CatalogEntry(["Mul 4"], "My Target", Type: "HII")]);
        Assert.Equal("My Target", store.LoadUserCatalog().Single().Name);
    }
}

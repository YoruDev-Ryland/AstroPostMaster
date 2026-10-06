using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.App.Tests;

public class AppStateTests
{
    [Fact]
    public void Load_EmptyFolder_GivesDefaultsAndBundledCatalog()
    {
        using var dir = new TempDir();
        var state = AppState.Load(new AppStore(new AppPaths(dir.Path)));

        Assert.Empty(state.Sites);
        Assert.Empty(state.Rigs);
        Assert.Empty(state.Software);
        Assert.Empty(state.HashtagSets);
        Assert.Equal(92, state.Settings.JpegQuality);
        Assert.True(state.Catalog.Count > 10_000);
    }

    [Fact]
    public void SaveProfiles_RoundTripsThroughLoad()
    {
        using var dir = new TempDir();
        var store = new AppStore(new AppPaths(dir.Path));
        var state = AppState.Load(store);
        state.Sites.Add(new Site("s1", "SFRO", 1, IsDefault: true));
        state.HashtagSets.Add(new HashtagSet("h1", "Core", ["#astro"]));
        state.SaveProfiles();

        var reloaded = AppState.Load(store);

        Assert.Equal("SFRO", Assert.Single(reloaded.Sites).Name);
        Assert.Equal("Core", Assert.Single(reloaded.HashtagSets).Name);
    }

    [Fact]
    public void Sources_ReflectCurrentCollections()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        state.Rigs.Add(new Rig("r2", "Refractor"));

        var sources = state.Sources();

        Assert.Equal(2, sources.Rigs.Count);
        Assert.Same(state.Catalog, sources.Catalog);
    }

    [Fact]
    public void ReloadCatalog_PicksUpUserTargets()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        state.Store.SaveUserCatalog([new AstroPostMaster.Core.Catalog.CatalogEntry(["Mul 4"], "Mystery Nebula")]);

        state.ReloadCatalog();

        Assert.Equal("Mystery Nebula", state.Catalog.Find("mul4")!.Name);
    }
}

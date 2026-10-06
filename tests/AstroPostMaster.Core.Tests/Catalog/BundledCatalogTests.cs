using AstroPostMaster.Core.Catalog;

namespace AstroPostMaster.Core.Tests.Catalog;

public class BundledCatalogTests
{
    private static readonly TargetCatalog Catalog = TargetCatalog.LoadBundled([]);

    [Fact]
    public void Bundled_ContainsAllNgcIcObjects() => Assert.True(Catalog.Count > 10_000, $"only {Catalog.Count} entries");

    [Theory]
    [InlineData("M31", "Andromeda Galaxy (M31)")]
    [InlineData("heart nebula", "Heart Nebula (IC 1805)")]
    [InlineData("Thors Helmet", "Thor's Helmet (NGC 2359)")]
    [InlineData("Sh2-155", "Cave Nebula (Sh2-155)")]
    [InlineData("Caldwell 31", "Flaming Star Nebula (IC 405)")]
    [InlineData("IC 11", "Pacman Nebula (NGC 281)")]
    [InlineData("flame nebula", "Flame Nebula (NGC 2024)")]
    [InlineData("horsehead nebula", "Horsehead Nebula (B33)")]
    [InlineData("war and peace nebula", "War and Peace Nebula (NGC 6357)")]
    [InlineData("NGC 7331", "NGC 7331")]
    [InlineData("moon", "Moon")]
    public void Bundled_ResolvesCommonTargets(string query, string display)
    {
        var entry = Catalog.Find(query);
        Assert.NotNull(entry);
        Assert.Equal(display, TargetCatalog.Display(entry));
    }
}

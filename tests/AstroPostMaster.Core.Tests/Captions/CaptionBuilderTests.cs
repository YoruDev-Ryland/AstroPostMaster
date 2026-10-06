using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Catalog;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Tests.Captions;

public class CaptionBuilderTests
{
    private static readonly CatalogEntry Heart = new(["IC 1805", "Sh2-190"], "Heart Nebula", [], "HII");

    private static CaptionSources Sources() => new(
        Sites: [new Site("s1", "SFRO", 1, IsDefault: true)],
        Rigs: [new Rig("r1", "Starlux", "SkyWatcher Starlux 190mn", "ASI2600mm Pro", "Scorpio 3nm LRGBSHO", "ZWO AM5")],
        Software: [new SoftwareSet("w1", "Default", ["Siril - Stacking, Mosaic Stitching", "Pix Insight - Astro Processing, Recomposition", "Photoshop - Recomposition"])],
        HashtagSets: [new HashtagSet("h1", "Core", ["#astrophotography", "#astro"]), new HashtagSet("h2", "Nebula", ["#nebula", "#astro"])],
        Catalog: new TargetCatalog([Heart], []));

    private static CaptionInput Input() => new()
    {
        TargetText = "Heart Nebula (IC 1805)",
        TargetCatalogId = "IC 1805",
        SiteId = "s1", RigId = "r1", SoftwareId = "w1",
        HashtagSetIds = ["h1", "h2"],
        Broadband = ["R", "G", "B"],
        Palette = "HOO",
        IntegrationText = "12h30m",
    };

    [Fact]
    public void Build_WithDefaultTemplate_ReproducesTheUsualPost()
    {
        var caption = CaptionBuilder.Build(Input(), Sources(), new AppSettings());

        Assert.Equal("""
            Heart Nebula (IC 1805)

            Acquisition:
            SFRO - Bortle 1
            Filters: RGBHOO
            Integration: 12h30m

            Equipment:
            Scope: SkyWatcher Starlux 190mn
            Camera: ASI2600mm Pro
            Filters: Scorpio 3nm LRGBSHO
            Mount: ZWO AM5

            Software:
            Siril - Stacking, Mosaic Stitching
            Pix Insight - Astro Processing, Recomposition
            Photoshop - Recomposition

            #heartnebula #ic1805 #astrophotography #astro #nebula
            """, caption);
    }

    [Fact]
    public void Build_NeverContainsKoFi()
    {
        var input = Input();
        input.Description = "Thanks for looking!";
        input.Dates = [new DateOnly(2026, 8, 18)];
        var caption = CaptionBuilder.Build(input, Sources(), new AppSettings { MoonLineEnabled = true });
        Assert.DoesNotContain("ko-fi", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntegrationRows_OverrideTextAndAddBreakdown()
    {
        var input = Input();
        input.IntegrationRows = [new IntegrationRow("Hα", 60, 300), new IntegrationRow("OIII", 40, 300)];
        var values = CaptionBuilder.BuildValues(input, Sources(), includeMoon: false);

        Assert.Equal("8h20m", values["integration"]);
        Assert.Equal("Hα: 60×300s (5h)\nOIII: 40×300s (3h20m)", values["integration.breakdown"]);
    }

    [Fact]
    public void UnparseableIntegrationText_IsKeptVerbatim()
    {
        var input = Input();
        input.IntegrationText = "about 12 hours";
        Assert.Equal("about 12 hours", CaptionBuilder.ResolveIntegration(input));
    }

    [Fact]
    public void FilterOverride_WinsOverChips()
    {
        var input = Input();
        input.FilterOverride = "HaRGB";
        Assert.Equal("HaRGB", CaptionBuilder.ResolveFilters(input));
    }

    [Fact]
    public void MissingRig_DropsTheEquipmentSection()
    {
        var input = Input();
        input.RigId = null;
        var caption = CaptionBuilder.Build(input, Sources(), new AppSettings());
        Assert.DoesNotContain("Equipment:", caption);
        Assert.Contains("Software:", caption);
    }

    [Fact]
    public void FreeTypedTarget_GetsAHashtagFromItsText()
    {
        var input = Input();
        input.TargetText = "C/2025 R2 SWAN";
        input.TargetCatalogId = null;
        input.ExtraHashtags = ["#comet", "#Astro"];
        Assert.Equal(new[] { "#c2025r2swan", "#astrophotography", "#astro", "#nebula", "#comet" }, CaptionBuilder.ResolveHashtags(input, Sources()));
    }

    [Fact]
    public void DatesAndMoon_RenderWhenEnabled()
    {
        var input = Input();
        input.Dates = [new DateOnly(2024, 4, 23)];
        var caption = CaptionBuilder.Build(input, Sources(), new AppSettings { MoonLineEnabled = true });
        Assert.Contains("Dates: Apr 23, 2024", caption);
        Assert.Contains("Moon: 100% illuminated", caption);

        var withoutMoon = CaptionBuilder.Build(input, Sources(), new AppSettings { MoonLineEnabled = false });
        Assert.DoesNotContain("Moon:", withoutMoon);
    }
}

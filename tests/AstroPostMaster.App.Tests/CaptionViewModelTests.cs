using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.App.Tests;

public class CaptionViewModelTests
{
    private sealed class Harness
    {
        public required CaptionViewModel Vm { get; init; }
        public required CaptionInput Input { get; init; }
        public int Changes;
    }

    private static Harness Create(TempDir dir)
    {
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        Harness? h = null;
        var vm = new CaptionViewModel(post.Caption, state, () => h!.Changes++);
        h = new Harness { Vm = vm, Input = post.Caption };
        return h;
    }

    [Fact]
    public void SelectTarget_SetsDisplayIdAndHashtags()
    {
        using var dir = new TempDir();
        var h = Create(dir);

        h.Vm.SelectTarget(h.Vm.Search("heart nebula")[0]);

        Assert.Equal("Heart Nebula (IC 1805)", h.Vm.TargetText);
        Assert.Equal("IC 1805", h.Input.TargetCatalogId);
        Assert.Contains("#heartnebula #ic1805", h.Vm.Preview);
        Assert.Equal(1, h.Changes);
    }

    [Fact]
    public void TypingAFreeTarget_ClearsTheCatalogMatch()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        h.Vm.SelectTarget(h.Vm.Search("M31")[0]);

        h.Vm.TargetText = "Comet C/2025 R2 SWAN";

        Assert.Null(h.Input.TargetCatalogId);
        Assert.StartsWith("Comet C/2025 R2 SWAN", h.Vm.Preview);
        Assert.Contains("#cometc2025r2swan", h.Vm.Preview);
    }

    [Fact]
    public void FilterChips_DrivePaletteAndFilterText()
    {
        using var dir = new TempDir();
        var h = Create(dir);

        foreach (var chip in h.Vm.BroadbandChips.Where(c => c.Key is "R" or "G" or "B")) chip.IsSelected = true;
        h.Vm.NarrowbandChips.Single(c => c.Key == "Ha").IsSelected = true;
        h.Vm.NarrowbandChips.Single(c => c.Key == "OIII").IsSelected = true;

        Assert.Equal("HOO", h.Vm.Palette);
        Assert.Equal("RGBHOO", h.Vm.FilterText);
        Assert.Contains("Filters: RGBHOO", h.Vm.Preview);
        Assert.Equal(new[] { "R", "G", "B" }, h.Input.Broadband);
        Assert.Equal(5, h.Changes);
    }

    [Fact]
    public void ChoosingAPalettePreset_OverridesTheChipDefault()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        h.Vm.NarrowbandChips.Single(c => c.Key == "Ha").IsSelected = true;
        h.Vm.NarrowbandChips.Single(c => c.Key == "OIII").IsSelected = true;
        h.Vm.NarrowbandChips.Single(c => c.Key == "SII").IsSelected = true;

        h.Vm.Palette = "HSO";

        Assert.Equal("HSO", h.Vm.FilterText);
    }

    [Fact]
    public void ManualFilterText_SticksUntilCleared()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        h.Vm.BroadbandChips.Single(c => c.Key == "L").IsSelected = true;

        h.Vm.FilterText = "HaLRGB";
        h.Vm.BroadbandChips.Single(c => c.Key == "R").IsSelected = true;
        Assert.Equal("HaLRGB", h.Vm.FilterText);
        Assert.Equal("HaLRGB", h.Input.FilterOverride);

        h.Vm.FilterText = "";
        Assert.Null(h.Input.FilterOverride);
        Assert.Equal("LR", h.Vm.FilterText);
    }

    [Fact]
    public void IntegrationRows_OverrideTypedTotal()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        h.Vm.IntegrationText = "12h30m";
        Assert.Contains("Integration: 12h30m", h.Vm.Preview);

        var row = h.Vm.AddIntegrationRow();
        row.Filter = "Hα";
        row.Subs = 60;
        row.SubSeconds = 300;

        Assert.Contains("Integration: 5h", h.Vm.Preview);
        Assert.Contains("Hα: 60×300s (5h)", h.Vm.Preview);
        Assert.Equal("5h", h.Vm.IntegrationTotal);

        h.Vm.RemoveIntegrationRow(row);
        Assert.Contains("Integration: 12h30m", h.Vm.Preview);
    }

    [Fact]
    public void DatesText_ParsesValidDates_AndFlagsBadOnes()
    {
        using var dir = new TempDir();
        var h = Create(dir);

        h.Vm.DatesText = "2026-08-18, 2026-08-19";
        Assert.False(h.Vm.DatesInvalid);
        Assert.Equal(new[] { new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 19) }, h.Input.Dates);
        Assert.Contains("Dates: Aug 18, 2026 · Aug 19, 2026", h.Vm.Preview);

        h.Vm.DatesText = "2026-08-18 yesterday";
        Assert.True(h.Vm.DatesInvalid);
        Assert.Equal(new[] { new DateOnly(2026, 8, 18) }, h.Input.Dates);
    }

    [Fact]
    public void UncheckingAHashtagSet_RemovesItsTags()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        Assert.Contains("#astrophotography", h.Vm.Preview);

        h.Vm.HashtagSets.Single(s => s.Key == "h1").IsSelected = false;
        h.Vm.HashtagSets.Single(s => s.Key == "h2").IsSelected = true;

        Assert.DoesNotContain("#astrophotography", h.Vm.Preview);
        Assert.Contains("#nebula", h.Vm.Preview);
        Assert.Equal(new[] { "h2" }, h.Input.HashtagSetIds);
    }

    [Fact]
    public void ExtraHashtags_AreParsedAndAppended()
    {
        using var dir = new TempDir();
        var h = Create(dir);

        h.Vm.ExtraHashtagsText = "#SpaceIsCool, deepsky";

        Assert.Equal(new[] { "#spaceiscool", "#deepsky" }, h.Input.ExtraHashtags);
        Assert.EndsWith("#spaceiscool #deepsky", h.Vm.Preview);
    }

    [Fact]
    public void ProfilesAreResolvedFromIds_AndChangeable()
    {
        using var dir = new TempDir();
        var h = Create(dir);
        Assert.Equal("SFRO", h.Vm.Site!.Name);

        h.Vm.Rig = null;

        Assert.Null(h.Input.RigId);
        Assert.DoesNotContain("Equipment:", h.Vm.Preview);
    }
}

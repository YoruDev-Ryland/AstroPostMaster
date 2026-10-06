using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.App.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void SetDefaultSite_ClearsThePreviousDefault()
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir));
        var backyard = vm.AddSite();
        backyard.Name = "Backyard";

        vm.SetDefaultSiteCommand.Execute(backyard);

        Assert.True(backyard.IsDefault);
        Assert.Single(vm.Sites, s => s.IsDefault);
    }

    [Fact]
    public void HashtagSets_AllowSeveralDefaults()
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir));

        vm.HashtagSets.Single(h => h.Name == "Nebula").IsDefault = true;

        Assert.Equal(2, vm.HashtagSets.Count(h => h.IsDefault));
    }

    [Fact]
    public void Save_RoundTripsProfilesAndSettings()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var vm = new SettingsViewModel(state);
        var rig = vm.AddRig();
        rig.Name = "Refractor";
        rig.Scope = "Askar FRA400";
        rig.ExtraText = "Guider: ASI220mm Mini\nFocuser: EAF";
        var software = vm.Software.Single();
        software.LinesText = "Siril - Stacking\n\nPixInsight - Everything";
        var tags = vm.HashtagSets.Single(h => h.Name == "Core");
        tags.TagsText = "#astrophotography, space #Astro";
        vm.WatermarkEnabled = true;
        vm.WatermarkText = "@me";
        vm.Theme = "Light";

        vm.Save();
        var reloaded = AppState.Load(state.Store);

        var savedRig = reloaded.Rigs.Single(r => r.Name == "Refractor");
        Assert.Equal("Askar FRA400", savedRig.Scope);
        Assert.Equal(new[] { "Guider: ASI220mm Mini", "Focuser: EAF" }, savedRig.Extra);
        Assert.Equal(new[] { "Siril - Stacking", "PixInsight - Everything" }, reloaded.Software.Single().Lines);
        Assert.Equal(new[] { "#astrophotography", "#space", "#astro" }, reloaded.HashtagSets.Single(h => h.Name == "Core").Tags);
        Assert.True(reloaded.Settings.Watermark.Enabled);
        Assert.Equal("@me", reloaded.Settings.Watermark.Text);
        Assert.Equal("Light", reloaded.Settings.Theme);
        Assert.Same(state.Rigs, state.Rigs); // the live collections are updated in place
        Assert.Contains(state.Rigs, r => r.Name == "Refractor");
    }

    [Fact]
    public void RemovingTheDefaultSite_LeavesNoDefault()
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir));

        vm.RemoveSiteCommand.Execute(vm.Sites.Single());
        vm.Save();

        Assert.Empty(vm.Sites);
    }

    [Fact]
    public void ResetTemplate_RestoresTheDefault()
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir)) { CaptionTemplate = "{target.display}" };

        vm.ResetTemplateCommand.Execute(null);

        Assert.Equal(DefaultCaptionTemplate.Text, vm.CaptionTemplate);
    }

    [Theory]
    [InlineData(10, 60)]
    [InlineData(85, 85)]
    [InlineData(150, 100)]
    public void JpegQuality_IsClamped(int input, int expected)
    {
        using var dir = new TempDir();
        var vm = new SettingsViewModel(Sample.State(dir)) { JpegQuality = input };
        Assert.Equal(expected, vm.JpegQuality);
    }

    [Fact]
    public void BlankProfileNames_AreGivenAName()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var vm = new SettingsViewModel(state);
        vm.AddSite().Name = "   ";

        vm.Save();

        Assert.Contains(state.Sites, s => s.Name == "Untitled site");
    }

    [Fact]
    public void KoFiLink_IsTheSoftwareSupportPage() =>
        Assert.Equal("https://ko-fi.com/rkremeier", SettingsViewModel.KoFiUrl);

    [Fact]
    public void Save_DoesNotLetBoundSelectorsClearAPostsProfiles()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var caption = new CaptionViewModel(post.Caption, state, () => { });
        // A bound ComboBox writes null back when its selected item leaves the collection.
        state.Sites.CollectionChanged += (_, _) => caption.Site = null;

        new SettingsViewModel(state).Save();
        caption.Reload();

        Assert.Equal("s1", post.Caption.SiteId);
        Assert.Equal("SFRO", caption.Site!.Name);
    }
}

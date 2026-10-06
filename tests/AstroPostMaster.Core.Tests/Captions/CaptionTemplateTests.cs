using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class CaptionTemplateTests
{
    private static Dictionary<string, string> V(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Render_SubstitutesValues()
    {
        var result = CaptionTemplate.Render("Target: {target.display}", V(("target.display", "Heart Nebula (IC 1805)")));
        Assert.Equal("Target: Heart Nebula (IC 1805)", result);
    }

    [Fact]
    public void Render_DropsLineWhosePlaceholdersAreAllEmpty_IncludingLabel()
    {
        var template = "Equipment:\nScope: {rig.scope}\nMount: {rig.mount}";
        var result = CaptionTemplate.Render(template, V(("rig.scope", "Starlux 190mn"), ("rig.mount", "")));
        Assert.Equal("Equipment:\nScope: Starlux 190mn", result);
    }

    [Fact]
    public void Render_DropsSectionWhosePlaceholdersAreAllEmpty_IncludingHeading()
    {
        var template = "{target.display}\n\nSoftware:\n{software}\n\n{hashtags}";
        var result = CaptionTemplate.Render(template, V(("target.display", "M31"), ("software", " "), ("hashtags", "#m31")));
        Assert.Equal("M31\n\n#m31", result);
    }

    [Fact]
    public void Render_KeepsSectionsWithoutPlaceholders()
    {
        var result = CaptionTemplate.Render("Clear skies!\n\n{hashtags}", V(("hashtags", "")));
        Assert.Equal("Clear skies!", result);
    }

    [Fact]
    public void Render_LeavesUnknownPlaceholdersLiteral()
    {
        var result = CaptionTemplate.Render("Hi {not.a.thing}", V());
        Assert.Equal("Hi {not.a.thing}", result);
    }

    [Fact]
    public void Render_DoesNotReexpandBracesInsideValues()
    {
        var result = CaptionTemplate.Render("{description}", V(("description", "Shot {site.name} with Hα × 300s 🌌"), ("site.name", "SFRO")));
        Assert.Equal("Shot {site.name} with Hα × 300s 🌌", result);
    }

    [Fact]
    public void Render_PlaceholderNamesAreCaseInsensitive()
    {
        var result = CaptionTemplate.Render("{Target.Display}", V(("target.display", "M42")));
        Assert.Equal("M42", result);
    }

    [Fact]
    public void Render_MultiLineValuesRenderOneItemPerLine()
    {
        var result = CaptionTemplate.Render("Software:\n{software}", V(("software", "Siril - Stacking\nPixInsight - Processing")));
        Assert.Equal("Software:\nSiril - Stacking\nPixInsight - Processing", result);
    }

    [Fact]
    public void Render_NormalizesCrLfAndCollapsesExtraBlankLines()
    {
        var result = CaptionTemplate.Render("A {a}\r\n\r\n\r\n\r\nB {b}\r\n", V(("a", "1"), ("b", "2")));
        Assert.Equal("A 1\n\nB 2", result);
    }

    [Fact]
    public void DefaultTemplate_UsesOnlyKnownPlaceholders_AndHasNoKoFi()
    {
        var used = System.Text.RegularExpressions.Regex.Matches(DefaultCaptionTemplate.Text, @"\{([a-z.]+)\}")
            .Select(m => m.Groups[1].Value);
        Assert.All(used, p => Assert.Contains(p, CaptionTemplate.Placeholders));
        Assert.DoesNotContain("ko-fi", DefaultCaptionTemplate.Text, StringComparison.OrdinalIgnoreCase);
    }
}

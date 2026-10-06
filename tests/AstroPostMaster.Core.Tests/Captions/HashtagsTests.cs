using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class HashtagsTests
{
    [Theory]
    [InlineData("#Andromeda Galaxy!", "#andromedagalaxy")]
    [InlineData("Thor's Helmet", "#thorshelmet")]
    [InlineData("deep_sky", "#deep_sky")]
    [InlineData("Hα", "#hα")]
    [InlineData("M 31", "#m31")]
    public void Normalize_LowercasesAndStripsPunctuation(string raw, string expected) =>
        Assert.Equal(expected, Hashtags.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("!!!")]
    [InlineData(null)]
    public void Normalize_ReturnsNullForEmpty(string? raw) => Assert.Null(Hashtags.Normalize(raw));

    [Fact]
    public void Merge_DedupesPreservingFirstOccurrence()
    {
        var merged = Hashtags.Merge(["M31", "#m31"], ["#Astro", "astro", "#nightsky"]);
        Assert.Equal(new[] { "#m31", "#astro", "#nightsky" }, merged);
    }

    [Fact]
    public void Split_AcceptsSpacesCommasAndNewlines() =>
        Assert.Equal(new[] { "#a", "#b", "#c", "#d" }, Hashtags.Split("#a #b, c\nd"));
}

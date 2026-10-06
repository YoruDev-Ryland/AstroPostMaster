using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class FilterStringTests
{
    [Theory]
    [InlineData(new[] { "B", "R", "G" }, "HOO", "RGBHOO")]
    [InlineData(new[] { "L", "R", "G", "B" }, "SHO", "LRGBSHO")]
    [InlineData(new string[0], "sho", "SHO")]
    [InlineData(new[] { "l" }, null, "L")]
    [InlineData(new[] { "R", "G", "B", "X" }, " ", "RGB")]
    public void Build_OrdersBroadbandThenAppendsPalette(string[] broadband, string? palette, string expected) =>
        Assert.Equal(expected, FilterString.Build(broadband, palette));

    [Theory]
    [InlineData(true, true, false, "HOO")]
    [InlineData(true, true, true, "SHO")]
    [InlineData(true, false, false, "H")]
    [InlineData(true, false, true, "SH")]
    [InlineData(false, false, false, "")]
    public void DefaultPalette_FollowsNarrowbandChips(bool ha, bool oiii, bool sii, string expected) =>
        Assert.Equal(expected, FilterString.DefaultPalette(ha, oiii, sii));
}

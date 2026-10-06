using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class MoonTests
{
    [Fact]
    public void FullMoonNight_IsNearlyFullyIlluminated() =>
        Assert.InRange(Moon.IlluminationPercentForNight(new DateOnly(2024, 4, 23)), 95, 100);

    [Fact]
    public void NewMoonNight_IsNearlyDark() =>
        Assert.InRange(Moon.IlluminationPercentForNight(new DateOnly(2024, 4, 8)), 0, 5);

    [Fact]
    public void FirstQuarterNight_IsAboutHalf() =>
        Assert.InRange(Moon.IlluminationPercentForNight(new DateOnly(2024, 4, 15)), 35, 65);

    [Fact]
    public void Describe_ShowsSingleValueOrRange()
    {
        Assert.Equal("100% illuminated", Moon.Describe([new DateOnly(2024, 4, 23)]));
        Assert.Equal("0–1% illuminated", Moon.Describe([new DateOnly(2024, 4, 9), new DateOnly(2024, 4, 8)]));
        Assert.Equal("", Moon.Describe([]));
    }
}

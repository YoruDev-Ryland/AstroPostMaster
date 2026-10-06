using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class CaptionStatsTests
{
    [Fact]
    public void CharacterCount_CountsCodePoints() => Assert.Equal(4, CaptionStats.CharacterCount("Hα 🌌"));

    [Fact]
    public void HashtagCount_CountsUnicodeTags() => Assert.Equal(3, CaptionStats.HashtagCount("#a #b text #hα and # alone"));
}

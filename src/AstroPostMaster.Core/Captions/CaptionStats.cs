using System.Text.RegularExpressions;

namespace AstroPostMaster.Core.Captions;

public static partial class CaptionStats
{
    public const int MaxCharacters = 2200;

    [GeneratedRegex(@"#[\p{L}\p{N}_]+")]
    private static partial Regex HashtagRegex();

    /// <summary>Counts Unicode code points, so an emoji counts once.</summary>
    public static int CharacterCount(string caption) => caption.EnumerateRunes().Count();

    public static int HashtagCount(string caption) => HashtagRegex().Count(caption);
}

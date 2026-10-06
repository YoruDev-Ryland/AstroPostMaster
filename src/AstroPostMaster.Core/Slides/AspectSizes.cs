using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Slides;

public static class AspectSizes
{
    /// <summary>All shapes in selector order: portrait, square, landscape.</summary>
    public static readonly IReadOnlyList<AspectRatio> All =
    [
        AspectRatio.Portrait4x5, AspectRatio.Portrait3x4, AspectRatio.Portrait2x3, AspectRatio.Square,
        AspectRatio.Landscape4x3, AspectRatio.Landscape3x2, AspectRatio.Landscape16x9, AspectRatio.Landscape191x100,
    ];

    /// <summary>One slide's width ÷ height.</summary>
    public static double Ratio(AspectRatio aspect) => aspect switch
    {
        AspectRatio.Portrait4x5 => 4.0 / 5,
        AspectRatio.Portrait3x4 => 3.0 / 4,
        AspectRatio.Portrait2x3 => 2.0 / 3,
        AspectRatio.Square => 1.0,
        AspectRatio.Landscape4x3 => 4.0 / 3,
        AspectRatio.Landscape3x2 => 3.0 / 2,
        AspectRatio.Landscape16x9 => 16.0 / 9,
        AspectRatio.Landscape191x100 => 1080.0 / 566, // Instagram's landscape limit, 1080×566
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, null),
    };

    public static string Label(AspectRatio aspect) => aspect switch
    {
        AspectRatio.Portrait4x5 => "4:5",
        AspectRatio.Portrait3x4 => "3:4",
        AspectRatio.Portrait2x3 => "2:3",
        AspectRatio.Square => "1:1",
        AspectRatio.Landscape4x3 => "4:3",
        AspectRatio.Landscape3x2 => "3:2",
        AspectRatio.Landscape16x9 => "16:9",
        AspectRatio.Landscape191x100 => "1.91:1",
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, null),
    };

    public static string Group(AspectRatio aspect) => Ratio(aspect) switch
    {
        < 1 => "Portrait",
        1 => "Square",
        _ => "Landscape",
    };

    /// <summary>Instagram's size for the shape: 1080 px wide.</summary>
    public static (int Width, int Height) Output(AspectRatio aspect) =>
        aspect == AspectRatio.Landscape191x100
            ? (1080, 566)
            : (1080, (int)Math.Round(1080 / Ratio(aspect), MidpointRounding.AwayFromZero));
}

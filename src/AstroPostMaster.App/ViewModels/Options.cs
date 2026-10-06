using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;

namespace AstroPostMaster.App.ViewModels;

public sealed record AspectOption(AspectRatio Aspect, string Label, string Group)
{
    public static readonly IReadOnlyList<AspectOption> All =
        AspectSizes.All.Select(a => new AspectOption(a, AspectSizes.Label(a), AspectSizes.Group(a))).ToList();

    public override string ToString() => Label;
}

public sealed record ExportSizeOption(ExportSize Size, string Label, string Hint)
{
    public static readonly IReadOnlyList<ExportSizeOption> All =
    [
        new(ExportSize.Full, "Full resolution", "Every pixel the frame covers, up to 8192 px"),
        new(ExportSize.LongEdge4096, "4096 px", "Long edge · X and other high-res sites"),
        new(ExportSize.LongEdge2048, "2048 px", "Long edge · Facebook, Bluesky"),
        new(ExportSize.Instagram1080, "1080 px", "Instagram's size; it shrinks anything larger"),
    ];

    public static int IndexOf(ExportSize size) => All.ToList().FindIndex(o => o.Size == size);

    public override string ToString() => Label;
}

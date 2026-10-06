using System.Text.Json.Serialization;
using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Model;

/// <summary>Slide shape. New members are appended so stored posts keep their meaning.</summary>
public enum AspectRatio { Portrait4x5, Portrait3x4, Square, Portrait2x3, Landscape4x3, Landscape3x2, Landscape16x9, Landscape191x100 }

/// <summary>Exported slide size. Full keeps every source pixel a frame covers (never upscaled, long edge ≤ 8192).</summary>
public enum ExportSize { Full, LongEdge4096, LongEdge2048, Instagram1080 }

public enum SlideKind { Crop, Panorama, Full }

/// <summary>A rectangle in fractions (0–1) of the source image's width and height.</summary>
public readonly record struct RectF(double X, double Y, double W, double H)
{
    [JsonIgnore] public double Right => X + W;
    [JsonIgnore] public double Bottom => Y + H;
    [JsonIgnore] public double CenterX => X + W / 2;
    [JsonIgnore] public double CenterY => Y + H / 2;
}

/// <summary>One entry in the carousel. A panorama expands to <see cref="Panels"/> consecutive slides on export.</summary>
public sealed record Slide(SlideKind Kind, RectF Rect, int Panels = 1)
{
    public static Slide FullImage => new(SlideKind.Full, new RectF(0, 0, 1, 1));
}

public sealed class CaptionInput
{
    /// <summary>The target as shown in the caption, e.g. "Andromeda Galaxy (M 31)". Filled from the catalog, editable.</summary>
    public string TargetText { get; set; } = "";
    /// <summary>Primary id of the matched catalog entry, or null for free-typed targets.</summary>
    public string? TargetCatalogId { get; set; }
    public string Description { get; set; } = "";
    public string? SiteId { get; set; }
    public string? RigId { get; set; }
    public string? SoftwareId { get; set; }
    public List<string> HashtagSetIds { get; set; } = [];
    public List<string> ExtraHashtags { get; set; } = [];
    public List<string> Broadband { get; set; } = [];
    public string? Palette { get; set; }
    /// <summary>When set, replaces the generated filter string.</summary>
    public string? FilterOverride { get; set; }
    public string IntegrationText { get; set; } = "";
    public List<IntegrationRow> IntegrationRows { get; set; } = [];
    public List<DateOnly> Dates { get; set; } = [];
}

public sealed class Post
{
    public string Id { get; set; } = NewId();
    public string Title { get; set; } = "Untitled";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? SourcePath { get; set; }
    public AspectRatio Aspect { get; set; } = AspectRatio.Portrait4x5;
    /// <summary>Per-post override of <see cref="AppSettings.ExportSize"/>; null follows the setting.</summary>
    public ExportSize? ExportSize { get; set; }
    /// <summary>A locked post is read-only (slides, image, caption) until unlocked.</summary>
    public bool IsLocked { get; set; }
    public List<Slide> Slides { get; set; } = [];
    public CaptionInput Caption { get; set; } = new();

    public static string NewId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
}

using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Model;

public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class WatermarkSettings
{
    public bool Enabled { get; set; }
    public string Text { get; set; } = "";
    public Corner Corner { get; set; } = Corner.BottomRight;
    /// <summary>Text height as a fraction of slide height (clamped to 0.01–0.1 when drawn).</summary>
    public double SizeFraction { get; set; } = 0.025;
    public double Opacity { get; set; } = 0.6;
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string? DefaultBrowseFolder { get; set; }
    public string CaptionTemplate { get; set; } = DefaultCaptionTemplate.Text;
    public int JpegQuality { get; set; } = 92;
    public ExportSize ExportSize { get; set; } = ExportSize.Full;
    public WatermarkSettings Watermark { get; set; } = new();
    public bool MoonLineEnabled { get; set; }
    /// <summary>Serve the phone page over self-signed HTTPS. Default follows the Task 1 spike decision.</summary>
    public bool UseHttps { get; set; } = true;
}

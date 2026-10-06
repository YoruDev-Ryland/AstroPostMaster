using System.Text;
using AstroPostMaster.Core.Model;
using SkiaSharp;

namespace AstroPostMaster.Imaging.Tests;

public class PostExporterTests
{
    private sealed class ListProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private static Post SamplePost(string sourcePath) => new()
    {
        Title = "M31",
        SourcePath = sourcePath,
        Aspect = AspectRatio.Portrait4x5,
        Slides =
        [
            new Slide(SlideKind.Crop, new RectF(0.3, 0.2, 0.25, 0.5)),
            new Slide(SlideKind.Panorama, new RectF(0, 0.2, 1, 0.4), 2),
            Slide.FullImage,
        ],
    };

    [Fact]
    public void Export_WritesNumberedSlidesAndCaption()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (x, y) => ((byte)x, (byte)y, 100));
        var progress = new ListProgress();

        var settings = new AppSettings { ExportSize = ExportSize.Instagram1080 };
        var result = PostExporter.Export(SamplePost(source), "Hα caption\n#m31", settings, dir.File("export"), progress);

        Assert.Equal(new[] { "01.jpg", "02.jpg", "03.jpg", "04.jpg" }, result.SlideFiles.Select(Path.GetFileName));
        foreach (var file in result.SlideFiles)
        {
            using var bitmap = SKBitmap.Decode(file);
            Assert.Equal((1080, 1350), (bitmap.Width, bitmap.Height));
        }
        Assert.Equal("Hα caption\n#m31", File.ReadAllText(result.CaptionFile, Encoding.UTF8));
        Assert.False(File.ReadAllBytes(result.CaptionFile).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(1.0, progress.Values[^1]);
        Assert.Equal(4, progress.Values.Count);
    }

    [Fact]
    public void Export_ReplacesThePreviousExport()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (_, _) => (1, 1, 1));
        var exportDir = dir.File("export");
        Directory.CreateDirectory(exportDir);
        File.WriteAllText(Path.Combine(exportDir, "99.jpg"), "stale");

        PostExporter.Export(SamplePost(source), "c", new AppSettings(), exportDir);

        Assert.False(File.Exists(Path.Combine(exportDir, "99.jpg")));
        Assert.True(File.Exists(Path.Combine(exportDir, "01.jpg")));
    }

    [Fact]
    public void Export_MissingSource_ThrowsAndLeavesNothingBehind()
    {
        using var dir = new TempDir();
        var exportDir = dir.File("export");

        Assert.Throws<FileNotFoundException>(() =>
            PostExporter.Export(SamplePost(dir.File("gone.jpg")), "c", new AppSettings(), exportDir));

        Assert.False(Directory.Exists(exportDir));
        Assert.Empty(Directory.GetDirectories(dir.Path));
    }

    [Fact]
    public void Export_CancelledMidway_LeavesNothingBehind()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (_, _) => (1, 1, 1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            PostExporter.Export(SamplePost(source), "c", new AppSettings(), dir.File("export"), cancellationToken: cts.Token));

        Assert.Empty(Directory.GetDirectories(dir.Path));
    }

    [Fact]
    public void CopyTo_CreatesTitledSubfolderWithoutOverwriting()
    {
        using var dir = new TempDir();
        var source = dir.File("src.jpg");
        Fixtures.WriteRgb(source, 3000, 2000, (_, _) => (1, 1, 1));
        var result = PostExporter.Export(SamplePost(source), "c", new AppSettings(), dir.File("export"));
        var target = dir.File("out");
        Directory.CreateDirectory(target);

        var first = PostExporter.CopyTo(result, target, "M31: Andromeda/Core");
        var second = PostExporter.CopyTo(result, target, "M31: Andromeda/Core");

        Assert.Equal("M31_ Andromeda_Core", Path.GetFileName(first));
        Assert.Equal("M31_ Andromeda_Core (2)", Path.GetFileName(second));
        Assert.Equal(5, Directory.GetFiles(second).Length);
    }
}

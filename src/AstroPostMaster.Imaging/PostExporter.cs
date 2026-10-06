using System.Text;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using SkiaSharp;

namespace AstroPostMaster.Imaging;

public sealed record ExportResult(string Directory, IReadOnlyList<string> SlideFiles, string CaptionFile);

public static class PostExporter
{
    /// <summary>
    /// Renders every slide to 01.jpg…NN.jpg plus caption.txt (UTF-8, no BOM). Work happens in a hidden sibling
    /// folder that replaces <paramref name="exportDir"/> only on success; nothing is left behind on failure.
    /// </summary>
    public static ExportResult Export(Post post, string caption, AppSettings settings, string exportDir,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (post.SourcePath is null) throw new InvalidOperationException("Post has no source image.");
        if (post.Slides.Count == 0) throw new InvalidOperationException("Post has no slides.");

        var (sourceWidth, sourceHeight) = ImageLoader.ReadSize(post.SourcePath);
        var size = post.ExportSize ?? settings.ExportSize;
        var scale = SlideGeometry.RequiredScale(post.Slides, post.Aspect, sourceWidth, sourceHeight, size);
        var total = PostValidator.SlideCount(post);
        var fullExportDir = Path.GetFullPath(exportDir);
        var parent = Path.GetDirectoryName(fullExportDir)!;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);

        try
        {
            using var source = ImageLoader.Load(post.SourcePath, scale);
            var names = new List<string>();
            foreach (var slide in post.Slides)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmaps = SlideRenderer.Render(source, slide, post.Aspect, settings.Watermark, size);
                try
                {
                    foreach (var bitmap in bitmaps)
                    {
                        var name = $"{names.Count + 1:00}.jpg";
                        using (var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(settings.JpegQuality, 1, 100)))
                        using (var file = File.Create(Path.Combine(staging, name)))
                            data.SaveTo(file);
                        names.Add(name);
                        progress?.Report((double)names.Count / total);
                    }
                }
                finally
                {
                    foreach (var bitmap in bitmaps) bitmap.Dispose();
                }
            }

            File.WriteAllText(Path.Combine(staging, "caption.txt"), caption, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (Directory.Exists(fullExportDir)) Directory.Delete(fullExportDir, recursive: true);
            Directory.Move(staging, fullExportDir);
            return new ExportResult(
                fullExportDir,
                names.Select(n => Path.Combine(fullExportDir, n)).ToList(),
                Path.Combine(fullExportDir, "caption.txt"));
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    /// <summary>Copies an export into a new subfolder of <paramref name="folder"/> named after the post. Never overwrites.</summary>
    public static string CopyTo(ExportResult export, string folder, string title)
    {
        var baseName = SafeName(title);
        var target = Path.Combine(folder, baseName);
        for (var n = 2; Directory.Exists(target); n++) target = Path.Combine(folder, $"{baseName} ({n})");
        Directory.CreateDirectory(target);
        foreach (var file in export.SlideFiles.Append(export.CaptionFile))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        return target;
    }

    internal static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "AstroPostMaster export" : cleaned;
    }
}

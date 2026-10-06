namespace AstroPostMaster.App.Services;

/// <param name="Image">A UI bitmap (Avalonia <c>Bitmap</c> in the app; null in tests). View models never inspect it.</param>
public sealed record PreviewResult(object? Image, int SourceWidth, int SourceHeight);

/// <summary>Loads a display preview of a source image. Throws FileNotFoundException or ImageLoadException.</summary>
public interface IPreviewLoader
{
    Task<PreviewResult> LoadAsync(string path, CancellationToken cancellationToken = default);
}

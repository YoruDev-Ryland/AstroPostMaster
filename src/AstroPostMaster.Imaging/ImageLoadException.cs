namespace AstroPostMaster.Imaging;

public sealed class ImageLoadException(string path, string reason, Exception? inner = null)
    : Exception($"Could not load '{System.IO.Path.GetFileName(path)}': {reason}", inner)
{
    public string Path { get; } = path;
}

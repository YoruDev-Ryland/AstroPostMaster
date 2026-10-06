namespace AstroPostMaster.Core.Storage;

public sealed class AppPaths(string root)
{
    public string Root { get; } = root;

    /// <summary>%APPDATA%\AstroPostMaster, ~/Library/Application Support/AstroPostMaster, or ~/.config/AstroPostMaster.</summary>
    public static AppPaths ForCurrentUser()
    {
        var baseDir = OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppPaths(Path.Combine(baseDir, "AstroPostMaster"));
    }

    public string Settings => Path.Combine(Root, "settings.json");
    public string Sites => Path.Combine(Root, "profiles", "sites.json");
    public string Rigs => Path.Combine(Root, "profiles", "rigs.json");
    public string Software => Path.Combine(Root, "profiles", "software.json");
    public string Hashtags => Path.Combine(Root, "profiles", "hashtags.json");
    public string UserCatalog => Path.Combine(Root, "catalog.user.json");
    public string PostsDir => Path.Combine(Root, "posts");
    /// <summary>Folder of one post. Rejects ids that could escape the posts folder.</summary>
    public string PostDir(string id)
    {
        if (!IsSafeId(id)) throw new ArgumentException($"'{id}' is not a valid post id.", nameof(id));
        return Path.Combine(PostsDir, id);
    }

    public static bool IsSafeId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id != "." && id != ".."
        && id.IndexOfAny(['/', '\\']) < 0 && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    public string PostFile(string id) => Path.Combine(PostDir(id), "post.json");
    public string ExportDir(string id) => Path.Combine(PostDir(id), "export");
    public string Certificate => Path.Combine(Root, "handoff-cert.pfx");
}

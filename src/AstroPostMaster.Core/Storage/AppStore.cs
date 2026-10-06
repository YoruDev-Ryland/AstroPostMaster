using AstroPostMaster.Core.Catalog;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Storage;

public sealed class AppStore(AppPaths paths)
{
    public AppPaths Paths { get; } = paths;

    /// <summary>Raised with the file path whenever an unreadable JSON file was moved aside to "*.bak".</summary>
    public event Action<string>? CorruptFileRecovered;

    public AppSettings LoadSettings() => Load(Paths.Settings, () => new AppSettings());
    public void SaveSettings(AppSettings settings) => JsonFile.SaveAtomic(Paths.Settings, settings);

    public List<Site> LoadSites() => Load(Paths.Sites, () => new List<Site>());
    public void SaveSites(IEnumerable<Site> sites) => JsonFile.SaveAtomic(Paths.Sites, sites.ToList());

    public List<Rig> LoadRigs() => Load(Paths.Rigs, () => new List<Rig>());
    public void SaveRigs(IEnumerable<Rig> rigs) => JsonFile.SaveAtomic(Paths.Rigs, rigs.ToList());

    public List<SoftwareSet> LoadSoftware() => Load(Paths.Software, () => new List<SoftwareSet>());
    public void SaveSoftware(IEnumerable<SoftwareSet> software) => JsonFile.SaveAtomic(Paths.Software, software.ToList());

    public List<HashtagSet> LoadHashtagSets() => Load(Paths.Hashtags, () => new List<HashtagSet>());
    public void SaveHashtagSets(IEnumerable<HashtagSet> sets) => JsonFile.SaveAtomic(Paths.Hashtags, sets.ToList());

    public List<CatalogEntry> LoadUserCatalog() => Load(Paths.UserCatalog, () => new List<CatalogEntry>());
    public void SaveUserCatalog(IEnumerable<CatalogEntry> entries) => JsonFile.SaveAtomic(Paths.UserCatalog, entries.ToList());

    /// <summary>All readable posts, most recently updated first. Corrupt post files are moved aside and skipped.</summary>
    public IReadOnlyList<Post> LoadPosts()
    {
        if (!Directory.Exists(Paths.PostsDir)) return [];
        var posts = new List<Post>();
        foreach (var dir in Directory.EnumerateDirectories(Paths.PostsDir))
        {
            var file = Path.Combine(dir, "post.json");
            if (!File.Exists(file)) continue;
            if (Load<Post?>(file, () => null) is not { } post) continue;
            // The folder name is the identity; an id inside the JSON may be stale (copied folder) or hostile.
            post.Id = Path.GetFileName(dir);
            posts.Add(post);
        }
        return posts.OrderByDescending(p => p.UpdatedUtc).ToList();
    }

    public void SavePost(Post post)
    {
        post.UpdatedUtc = DateTimeOffset.UtcNow;
        JsonFile.SaveAtomic(Paths.PostFile(post.Id), post);
    }

    public void DeletePost(string id)
    {
        var dir = Paths.PostDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private T Load<T>(string path, Func<T> fallback) =>
        JsonFile.LoadOrDefault(path, fallback, p => CorruptFileRecovered?.Invoke(p));
}

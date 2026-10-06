using System.Text.Json;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.Core.Model;

public static class PostFactory
{
    public static Post CreateNew(
        IReadOnlyList<Site> sites, IReadOnlyList<Rig> rigs, IReadOnlyList<SoftwareSet> software, IReadOnlyList<HashtagSet> hashtagSets)
    {
        var post = new Post();
        post.Caption.SiteId = Profiles.DefaultOf(sites)?.Id;
        post.Caption.RigId = Profiles.DefaultOf(rigs)?.Id;
        post.Caption.SoftwareId = Profiles.DefaultOf(software)?.Id;
        post.Caption.HashtagSetIds = hashtagSets.Where(h => h.IsDefault).Select(h => h.Id).ToList();
        post.Slides.Add(Slide.FullImage);
        return post;
    }

    /// <summary>Deep, unlocked copy of everything (image, slides, shape, caption), as a starting point for a new post.</summary>
    public static Post Duplicate(Post source)
    {
        var copy = JsonSerializer.Deserialize<Post>(JsonSerializer.Serialize(source, Json.Options), Json.Options)!;
        copy.Id = Post.NewId();
        copy.Title = source.Title + " copy";
        copy.CreatedUtc = copy.UpdatedUtc = DateTimeOffset.UtcNow;
        copy.IsLocked = false;
        return copy;
    }
}

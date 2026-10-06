namespace AstroPostMaster.Core.Model;

public interface IProfile
{
    string Id { get; }
    string Name { get; }
    bool IsDefault { get; }
}

public sealed record Site(string Id, string Name, int? Bortle = null, bool IsDefault = false) : IProfile
{
    public string Describe() => Bortle is int b ? $"{Name} - Bortle {b}" : Name;
}

public sealed record Rig(
    string Id,
    string Name,
    string Scope = "",
    string Camera = "",
    string Filters = "",
    string Mount = "",
    IReadOnlyList<string>? Extra = null,
    bool IsDefault = false) : IProfile;

public sealed record SoftwareSet(string Id, string Name, IReadOnlyList<string>? Lines = null, bool IsDefault = false) : IProfile;

public sealed record HashtagSet(string Id, string Name, IReadOnlyList<string>? Tags = null, bool IsDefault = false) : IProfile;

public static class Profiles
{
    /// <summary>The profile flagged default, else the first one, else null.</summary>
    public static T? DefaultOf<T>(IEnumerable<T> items) where T : class, IProfile =>
        items.FirstOrDefault(p => p.IsDefault) ?? items.FirstOrDefault();

    public static T? ById<T>(IEnumerable<T> items, string? id) where T : class, IProfile =>
        id is null ? null : items.FirstOrDefault(p => p.Id == id);

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

using System.Text;
using System.Text.Json;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.Core.Catalog;

public sealed class TargetCatalog
{
    public const string ResourceName = "AstroPostMaster.Core.catalog.json";

    private readonly List<(CatalogEntry Entry, string[] Keys)> _indexed;
    private readonly Dictionary<string, CatalogEntry> _byKey = new(StringComparer.Ordinal);

    /// <summary>User entries come first and replace any bundled entry sharing one of their ids.</summary>
    public TargetCatalog(IEnumerable<CatalogEntry> bundled, IEnumerable<CatalogEntry> user)
    {
        var userList = user.ToList();
        var userKeys = userList.SelectMany(e => e.Ids).Select(Key).ToHashSet();
        var entries = userList.Concat(bundled.Where(e => !e.Ids.Any(id => userKeys.Contains(Key(id)))));
        _indexed = entries.Select(e => (e, KeysOf(e).ToArray())).ToList();

        // Ids win over names and names over aliases, so an alias can never shadow another object's designation.
        foreach (var (e, _) in _indexed) foreach (var id in e.Ids) _byKey.TryAdd(Key(id), e);
        foreach (var (e, _) in _indexed) if (e.Name.Length > 0) _byKey.TryAdd(Key(e.Name), e);
        foreach (var (e, _) in _indexed) foreach (var alias in e.Aliases ?? []) _byKey.TryAdd(Key(alias), e);
    }

    public int Count => _indexed.Count;

    public static TargetCatalog LoadBundled(IEnumerable<CatalogEntry> user) => new(LoadBundledEntries(), user);

    public static IReadOnlyList<CatalogEntry> LoadBundledEntries()
    {
        using var stream = typeof(TargetCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream, Json.Options) ?? [];
    }

    public CatalogEntry? Find(string query)
    {
        var key = Key(query);
        return key.Length == 0 ? null : _byKey.GetValueOrDefault(key);
    }

    /// <summary>Exact match first, then entries with a key starting with the query, then containing it.</summary>
    public IReadOnlyList<CatalogEntry> Search(string query, int max = 10)
    {
        var key = Key(query);
        var results = new List<CatalogEntry>();
        if (key.Length == 0) return results;
        if (_byKey.TryGetValue(key, out var exact)) results.Add(exact);
        Collect(keys => keys.Any(k => k.StartsWith(key, StringComparison.Ordinal)));
        Collect(keys => keys.Any(k => k.Contains(key, StringComparison.Ordinal)));
        return results;

        void Collect(Func<string[], bool> matches)
        {
            foreach (var (entry, keys) in _indexed)
            {
                if (results.Count >= max) return;
                if (matches(keys) && !results.Any(r => ReferenceEquals(r, entry))) results.Add(entry);
            }
        }
    }

    /// <summary>"Andromeda Galaxy (M31)"; just the id when unnamed; just the name when it equals the id.</summary>
    public static string Display(CatalogEntry e)
    {
        if (e.Name.Length == 0) return e.PrimaryId;
        return Key(e.Name) == Key(e.PrimaryId) ? e.Name : $"{e.Name} ({e.PrimaryId})";
    }

    public static IReadOnlyList<string> DefaultHashtags(CatalogEntry e) =>
        e.Hashtags is { Count: > 0 } custom
            ? Captions.Hashtags.Merge(custom)
            : Captions.Hashtags.Merge([e.Name, e.PrimaryId]);

    /// <summary>Lowercase letters and digits only; leading zeros stripped from each digit run ("NGC 0224" → "ngc224").</summary>
    public static string Key(string text)
    {
        var filtered = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch)) filtered.Append(char.ToLowerInvariant(ch));

        var s = filtered.ToString();
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            if (!char.IsDigit(s[i])) { sb.Append(s[i++]); continue; }
            var start = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            var run = s.AsSpan(start, i - start).TrimStart('0');
            if (run.IsEmpty) sb.Append('0'); else sb.Append(run);
        }
        return sb.ToString();
    }

    private static IEnumerable<string> KeysOf(CatalogEntry e) =>
        e.Ids.Append(e.Name).Concat(e.Aliases ?? []).Where(s => s.Length > 0).Select(Key);
}

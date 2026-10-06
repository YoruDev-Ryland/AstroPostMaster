using System.Text;

namespace AstroPostMaster.Core.Captions;

public static class Hashtags
{
    public const int InstagramLimit = 30;

    /// <summary>Lowercases, keeps letters/digits/underscore, prefixes '#'. Returns null when nothing is left.</summary>
    public static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var sb = new StringBuilder(raw.Length + 1);
        foreach (var ch in raw)
            if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(char.ToLowerInvariant(ch));
        return sb.Length == 0 ? null : "#" + sb;
    }

    /// <summary>Normalizes every tag and de-duplicates, keeping the first occurrence's position.</summary>
    public static IReadOnlyList<string> Merge(params IEnumerable<string>[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var group in groups)
            foreach (var raw in group)
                if (Normalize(raw) is { } tag && seen.Add(tag)) result.Add(tag);
        return result;
    }

    /// <summary>Parses a user-typed tag list separated by spaces, commas or newlines.</summary>
    public static IReadOnlyList<string> Split(string text) =>
        Merge(text.Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
}

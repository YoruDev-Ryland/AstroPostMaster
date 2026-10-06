using System.Text.RegularExpressions;

namespace AstroPostMaster.Core.Captions;

/// <summary>
/// Renders caption templates containing {placeholders}. A line whose placeholders all render empty is dropped
/// (labels included); a blank-line-separated section whose placeholders all render empty is dropped
/// (headings included). Substitution is single-pass, so braces inside values are never re-expanded.
/// </summary>
public static partial class CaptionTemplate
{
    public static readonly IReadOnlyList<string> Placeholders =
    [
        "target.display", "target.name", "target.catalog",
        "description",
        "site", "site.name", "site.bortle",
        "filters", "integration", "integration.breakdown", "dates", "moon",
        "rig.name", "rig.scope", "rig.camera", "rig.filters", "rig.mount", "rig.extra",
        "software",
        "hashtags",
    ];

    [GeneratedRegex(@"\{([A-Za-z]+(?:\.[A-Za-z]+)*)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\n(?:[ \t]*\n)+")]
    private static partial Regex SectionBreakRegex();

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        var normalized = template.Replace("\r\n", "\n").Replace('\r', '\n');
        var kept = new List<string>();
        foreach (var section in SectionBreakRegex().Split(normalized))
        {
            var rendered = RenderSection(section, values);
            if (!string.IsNullOrWhiteSpace(rendered)) kept.Add(rendered.Trim('\n'));
        }
        return string.Join("\n\n", kept).Trim();
    }

    private static string? RenderSection(string section, IReadOnlyDictionary<string, string> values)
    {
        var sectionHasPlaceholder = false;
        var sectionHasValue = false;
        var lines = new List<string>();

        foreach (var line in section.Split('\n'))
        {
            var lineHasPlaceholder = false;
            var lineHasValue = false;
            var rendered = PlaceholderRegex().Replace(line, match =>
            {
                if (!values.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var value)) return match.Value;
                lineHasPlaceholder = true;
                if (string.IsNullOrWhiteSpace(value)) return string.Empty;
                lineHasValue = true;
                return value.Trim();
            });

            sectionHasPlaceholder |= lineHasPlaceholder;
            sectionHasValue |= lineHasValue;
            if (lineHasPlaceholder && !lineHasValue) continue;
            lines.Add(rendered.TrimEnd());
        }

        if (sectionHasPlaceholder && !sectionHasValue) return null;
        return string.Join('\n', lines);
    }
}

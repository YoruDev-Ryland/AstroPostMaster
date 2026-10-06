using System.Globalization;
using AstroPostMaster.Core.Catalog;
using AstroPostMaster.Core.Model;

namespace AstroPostMaster.Core.Captions;

public sealed record CaptionSources(
    IReadOnlyList<Site> Sites,
    IReadOnlyList<Rig> Rigs,
    IReadOnlyList<SoftwareSet> Software,
    IReadOnlyList<HashtagSet> HashtagSets,
    TargetCatalog Catalog);

public static class CaptionBuilder
{
    public static string Build(CaptionInput input, CaptionSources sources, AppSettings settings) =>
        CaptionTemplate.Render(settings.CaptionTemplate, BuildValues(input, sources, settings.MoonLineEnabled));

    public static IReadOnlyDictionary<string, string> BuildValues(CaptionInput input, CaptionSources sources, bool includeMoon)
    {
        var entry = input.TargetCatalogId is { } id ? sources.Catalog.Find(id) : null;
        var site = Profiles.ById(sources.Sites, input.SiteId);
        var rig = Profiles.ById(sources.Rigs, input.RigId);
        var software = Profiles.ById(sources.Software, input.SoftwareId);
        var dates = input.Dates.Distinct().Order().ToList();

        return new Dictionary<string, string>
        {
            ["target.display"] = input.TargetText.Trim(),
            ["target.name"] = entry?.Name ?? "",
            ["target.catalog"] = entry?.PrimaryId ?? "",
            ["description"] = input.Description.Trim(),
            ["site"] = site?.Describe() ?? "",
            ["site.name"] = site?.Name ?? "",
            ["site.bortle"] = site?.Bortle?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["filters"] = ResolveFilters(input),
            ["integration"] = ResolveIntegration(input),
            ["integration.breakdown"] = string.Join('\n', input.IntegrationRows.Select(Integration.FormatRow)),
            ["dates"] = string.Join(" · ", dates.Select(d => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture))),
            ["moon"] = includeMoon ? Moon.Describe(dates) : "",
            ["rig.name"] = rig?.Name ?? "",
            ["rig.scope"] = rig?.Scope ?? "",
            ["rig.camera"] = rig?.Camera ?? "",
            ["rig.filters"] = rig?.Filters ?? "",
            ["rig.mount"] = rig?.Mount ?? "",
            ["rig.extra"] = string.Join('\n', rig?.Extra ?? []),
            ["software"] = string.Join('\n', software?.Lines ?? []),
            ["hashtags"] = string.Join(' ', ResolveHashtags(input, sources)),
        };
    }

    /// <summary>Target tags, then each selected set in order, then extra tags; normalized and de-duplicated.</summary>
    public static IReadOnlyList<string> ResolveHashtags(CaptionInput input, CaptionSources sources)
    {
        var entry = input.TargetCatalogId is { } id ? sources.Catalog.Find(id) : null;
        IEnumerable<string> targetTags = entry is not null
            ? TargetCatalog.DefaultHashtags(entry)
            : string.IsNullOrWhiteSpace(input.TargetText) ? [] : [input.TargetText];
        var setTags = input.HashtagSetIds
            .Select(setId => Profiles.ById(sources.HashtagSets, setId))
            .OfType<HashtagSet>()
            .SelectMany(set => set.Tags ?? []);
        return Hashtags.Merge(targetTags, setTags, input.ExtraHashtags);
    }

    public static string ResolveFilters(CaptionInput input) =>
        string.IsNullOrWhiteSpace(input.FilterOverride)
            ? FilterString.Build(input.Broadband, input.Palette)
            : input.FilterOverride.Trim();

    /// <summary>Per-filter rows win; otherwise parsed text is normalized ("12h 30m" → "12h30m"); otherwise text is kept as typed.</summary>
    public static string ResolveIntegration(CaptionInput input)
    {
        if (input.IntegrationRows.Count > 0) return Integration.Format(Integration.Total(input.IntegrationRows));
        return Integration.TryParse(input.IntegrationText, out var parsed) ? Integration.Format(parsed) : input.IntegrationText.Trim();
    }
}

using System.Text.Json.Serialization;

namespace AstroPostMaster.Core.Catalog;

/// <summary>A deep-sky (or solar-system) target. Ids[0] is the primary designation, e.g. "M31", "IC 1805", "Sh2-155".</summary>
public sealed record CatalogEntry(
    IReadOnlyList<string> Ids,
    string Name,
    IReadOnlyList<string>? Aliases = null,
    string Type = "",
    IReadOnlyList<string>? Hashtags = null)
{
    [JsonIgnore]
    public string PrimaryId => Ids.Count > 0 ? Ids[0] : Name;
}

namespace AstroPostMaster.Core.Captions;

/// <summary>Builds compact filter strings like "RGBHOO": broadband in L,R,G,B order, then the narrowband palette.</summary>
public static class FilterString
{
    private static readonly string[] BroadbandOrder = ["L", "R", "G", "B"];

    public static readonly IReadOnlyList<string> PalettePresets = ["SHO", "HOO", "HSO", "HOS", "OHS", "HO"];

    public static string Build(IEnumerable<string> broadband, string? palette)
    {
        var selected = new HashSet<string>(broadband.Select(b => b.Trim().ToUpperInvariant()));
        var prefix = string.Concat(BroadbandOrder.Where(selected.Contains));
        return prefix + (palette?.Trim().ToUpperInvariant() ?? string.Empty);
    }

    /// <summary>Hα+OIII is the common bicolor "HOO"; any other combination lists S, H, O in that order.</summary>
    public static string DefaultPalette(bool ha, bool oiii, bool sii) =>
        ha && oiii && !sii ? "HOO" : (sii ? "S" : "") + (ha ? "H" : "") + (oiii ? "O" : "");
}

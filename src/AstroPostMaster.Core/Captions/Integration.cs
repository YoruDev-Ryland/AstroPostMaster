using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AstroPostMaster.Core.Captions;

public sealed record IntegrationRow(string Filter, int Subs, int SubSeconds)
{
    [JsonIgnore]
    public TimeSpan Total => TimeSpan.FromSeconds((long)Subs * SubSeconds);
}

public static partial class Integration
{
    [GeneratedRegex(@"^\s*(?:(?<h>\d+(?:\.\d+)?)\s*h)?\s*(?:(?<m>\d+)\s*m(?:in)?)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex HoursMinutesRegex();

    [GeneratedRegex(@"^\s*(?<h>\d+):(?<m>[0-5]\d)\s*$")]
    private static partial Regex ColonRegex();

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            return TryParseCore(text, out value);
        }
        catch (OverflowException)
        {
            value = TimeSpan.Zero;
            return false;
        }
    }

    private static bool TryParseCore(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;

        var colon = ColonRegex().Match(text);
        if (colon.Success)
        {
            value = TimeSpan.FromHours(int.Parse(colon.Groups["h"].Value, CultureInfo.InvariantCulture))
                  + TimeSpan.FromMinutes(int.Parse(colon.Groups["m"].Value, CultureInfo.InvariantCulture));
            return true;
        }

        var hm = HoursMinutesRegex().Match(text);
        if (!hm.Success || (!hm.Groups["h"].Success && !hm.Groups["m"].Success)) return false;
        var hours = hm.Groups["h"].Success ? double.Parse(hm.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = hm.Groups["m"].Success ? int.Parse(hm.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
        return true;
    }

    public static string Format(TimeSpan t)
    {
        var totalMinutes = (long)Math.Round(t.TotalMinutes, MidpointRounding.AwayFromZero);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes}m";
        return minutes == 0 ? $"{hours}h" : $"{hours}h{minutes}m";
    }

    public static TimeSpan Total(IEnumerable<IntegrationRow> rows) =>
        rows.Aggregate(TimeSpan.Zero, (sum, row) => sum + row.Total);

    public static string FormatRow(IntegrationRow row) =>
        $"{row.Filter}: {row.Subs}×{row.SubSeconds}s ({Format(row.Total)})";
}

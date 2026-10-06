namespace AstroPostMaster.Core.Captions;

/// <summary>Mean-synodic-month moon phase. Accurate to a few percent, which is enough for a caption line.</summary>
public static class Moon
{
    private static readonly DateTime KnownNewMoonUtc = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
    private const double SynodicMonthDays = 29.530588853;

    public static double Illumination(DateTime utc)
    {
        var days = (utc - KnownNewMoonUtc).TotalDays;
        var age = ((days % SynodicMonthDays) + SynodicMonthDays) % SynodicMonthDays;
        return (1 - Math.Cos(2 * Math.PI * age / SynodicMonthDays)) / 2;
    }

    /// <summary>Evaluated at 00:00 UTC after the night's date, approximating the middle of a night in the Americas/Europe.</summary>
    public static int IlluminationPercentForNight(DateOnly night) =>
        (int)Math.Round(100 * Illumination(night.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));

    public static string Describe(IEnumerable<DateOnly> nights)
    {
        var percents = nights.Distinct().Select(IlluminationPercentForNight).ToList();
        if (percents.Count == 0) return "";
        var (min, max) = (percents.Min(), percents.Max());
        return min == max ? $"{min}% illuminated" : $"{min}–{max}% illuminated";
    }
}

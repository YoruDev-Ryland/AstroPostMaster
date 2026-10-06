using AstroPostMaster.Core.Captions;

namespace AstroPostMaster.Core.Tests.Captions;

public class IntegrationTests
{
    [Theory]
    [InlineData("12h30m", 12, 30)]
    [InlineData("12h 30m", 12, 30)]
    [InlineData("  3H15M ", 3, 15)]
    [InlineData("12h", 12, 0)]
    [InlineData("45m", 0, 45)]
    [InlineData("45min", 0, 45)]
    [InlineData("12:30", 12, 30)]
    [InlineData("12.5h", 12, 30)]
    [InlineData("30h", 30, 0)]
    public void TryParse_AcceptsCommonForms(string text, int hours, int minutes)
    {
        Assert.True(Integration.TryParse(text, out var value));
        Assert.Equal(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12x")]
    [InlineData("12:75")]
    [InlineData(null)]
    public void TryParse_RejectsGarbage(string? text) => Assert.False(Integration.TryParse(text, out _));

    [Theory]
    [InlineData(12 * 60 + 30, "12h30m")]
    [InlineData(12 * 60, "12h")]
    [InlineData(45, "45m")]
    [InlineData(0, "0m")]
    public void Format_UsesHoursAndMinutes(int minutes, string expected) =>
        Assert.Equal(expected, Integration.Format(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Format_RoundsToNearestMinute() =>
        Assert.Equal("2h", Integration.Format(new TimeSpan(1, 59, 40)));

    [Fact]
    public void Total_SumsRows()
    {
        var rows = new[] { new IntegrationRow("Hα", 60, 300), new IntegrationRow("OIII", 40, 300) };
        Assert.Equal("8h20m", Integration.Format(Integration.Total(rows)));
    }

    [Fact]
    public void FormatRow_ShowsSubsLengthAndTotal() =>
        Assert.Equal("Hα: 60×300s (5h)", Integration.FormatRow(new IntegrationRow("Hα", 60, 300)));

    [Theory]
    [InlineData("99999999999m")]
    [InlineData("9999999999999999h")]
    [InlineData("99999999999:30")]
    public void TryParse_RejectsOverflowInsteadOfThrowing(string text) => Assert.False(Integration.TryParse(text, out _));
}

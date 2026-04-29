using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class AnalyticsTests
{
    [Fact]
    public void AnalyticsMath_ComputesPercentagesSafely()
    {
        Assert.Equal(0m, AnalyticsMath.Percentage(5, 0));
        Assert.Equal(25m, AnalyticsMath.Percentage(1, 4));
        Assert.Equal(33.3333m, AnalyticsMath.Percentage(1, 3));
    }

    [Fact]
    public void AnalyticsMath_ComputesNullableDifferences()
    {
        Assert.Equal(0.02m, AnalyticsMath.Difference(0.77m, 0.75m));
        Assert.Null(AnalyticsMath.Difference(null, 0.75m));
        Assert.Null(AnalyticsMath.Difference(0.77m, null));
    }

    [Fact]
    public void CsvFormatter_EscapesCommasQuotesAndLineBreaks()
    {
        var row = CsvFormatter.FormatRow([
            "plain",
            "comma,value",
            "quote\"value",
            "line\nvalue",
            0.75m,
            new DateOnly(2026, 4, 29)
        ]);

        Assert.Equal("plain,\"comma,value\",\"quote\"\"value\",\"line\nvalue\",0.75,2026-04-29", row);
    }
}

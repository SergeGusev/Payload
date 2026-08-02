using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class PostgresPaperPositionsScanTelemetryTests
{
    [Fact]
    public void Delta_SubtractsTransactionLocalCounters()
    {
        var before = new PostgresPaperPositionsScanStats(3, 120);
        var after = new PostgresPaperPositionsScanStats(5, 1_970);

        var delta = PostgresPaperPositionsScanStats.Delta(before, after);

        Assert.True(delta.HasValue);
        Assert.Equal(new PostgresPaperPositionsScanStats(2, 1_850), delta.Value);
    }

    [Fact]
    public void Delta_ReturnsUnmeasuredWhenCounterDecreases()
    {
        var before = new PostgresPaperPositionsScanStats(3, 120);
        var after = new PostgresPaperPositionsScanStats(2, 120);

        Assert.Null(PostgresPaperPositionsScanStats.Delta(before, after));
    }
}

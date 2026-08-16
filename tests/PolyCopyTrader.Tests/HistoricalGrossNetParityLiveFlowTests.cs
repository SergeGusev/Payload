namespace PolyCopyTrader.Tests;

public sealed class HistoricalGrossNetParityLiveFlowTests
{
    [Fact]
    public void OrdinaryLiveSettlement_ExplicitlyFlowsReturnedRowVersionIntoBalanceCas()
    {
        var source = ReadRepositorySource(
            "src",
            "PolyCopyTrader.Service",
            "LiveTrading",
            "LiveTradingProcessor.cs");
        var start = source.IndexOf(
            "private async Task<int> SettleMatchedOrdersAsync",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf(
            "private async Task TrySyncPaperShadowBeforeLiveSettlementAsync",
            start,
            StringComparison.Ordinal);

        Assert.True(end > start);
        var method = source[start..end];
        Assert.Contains(
            "settlementOrder = await repository.UpdateLiveOrderWithConcurrencyAsync(",
            method,
            StringComparison.Ordinal);
        Assert.Contains(
            "repository.ApplyLiveOrderSettlementToStrategyBalanceWithConcurrencyAsync(",
            method,
            StringComparison.Ordinal);
        Assert.Contains("settlementOrder.RowVersion", method, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "repository.ApplyLiveOrderSettlementToStrategyBalanceAsync(",
            method,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AsyncLocal", method, StringComparison.Ordinal);
    }

    private static string ReadRepositorySource(params string[] pathParts)
    {
        var configuredRoot = Environment.GetEnvironmentVariable("POLYCOPYTRADER_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            var configuredPath = Path.GetFullPath(Path.Combine(configuredRoot, Path.Combine(pathParts)));
            if (File.Exists(configuredPath))
            {
                return File.ReadAllText(configuredPath);
            }
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, Path.Combine(pathParts));
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository source file '{Path.Combine(pathParts)}' was not found from '{AppContext.BaseDirectory}'.");
    }
}

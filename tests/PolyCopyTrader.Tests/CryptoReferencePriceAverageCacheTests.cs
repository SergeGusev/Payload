using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class CryptoReferencePriceAverageCacheTests
{
    [Fact]
    public void Reset_BuildsConfiguredWindowAveragesWithProportionalSteps()
    {
        var now = new DateTimeOffset(2026, 6, 23, 12, 20, 0, TimeSpan.Zero);
        var cache = new CryptoReferencePriceAverageCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = ["BTC"],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60,
            WindowMinutes = [20, 10]
        });
        var ticks = Enumerable.Range(0, 120)
            .Select(index => Tick("BTC", now.AddSeconds(-1190 + index * 10), 100m + index))
            .ToArray();

        cache.Reset(ticks, now);

        var snapshot = cache.GetSnapshot();
        var tenMinute = snapshot.Averages.Single(average => average.AssetSymbol == "BTC" && average.WindowLabel == "10m");
        var twentyMinute = snapshot.Averages.Single(average => average.AssetSymbol == "BTC" && average.WindowLabel == "20m");
        Assert.Equal(10, tenMinute.SampleStepSeconds);
        Assert.Equal(20, twentyMinute.SampleStepSeconds);
        Assert.Equal(60, tenMinute.ExpectedSampleCount);
        Assert.Equal(60, tenMinute.SampleCount);
        Assert.True(tenMinute.IsFullWindow);
        Assert.Equal(189.5m, tenMinute.AveragePriceUsd);
    }

    [Fact]
    public void Add_UpdatesRollingAverageAndTrimsExpiredBucket()
    {
        var now = new DateTimeOffset(2026, 6, 23, 12, 20, 0, TimeSpan.Zero);
        var cache = new CryptoReferencePriceAverageCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = ["ETH"],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60,
            WindowMinutes = [10]
        });
        var ticks = Enumerable.Range(0, 60)
            .Select(index => Tick("ETH", now.AddSeconds(-590 + index * 10), 100m + index))
            .ToArray();
        cache.Reset(ticks, now);

        cache.Add(Tick("ETH", now.AddSeconds(10), 220m), now.AddSeconds(10));

        var average = cache.GetAverage("ETH", "10m");
        Assert.NotNull(average);
        Assert.Equal(60, average.SampleCount);
        Assert.True(average.IsFullWindow);
        Assert.Equal(131.5m, average.AveragePriceUsd);
        Assert.Equal(now.AddSeconds(-580), average.FirstBucketStartUtc);
        Assert.Equal(now.AddSeconds(10), average.LastBucketStartUtc);
    }

    private static CryptoReferencePriceTick Tick(
        string assetSymbol,
        DateTimeOffset sampledAtUtc,
        decimal priceUsd)
    {
        return new CryptoReferencePriceTick(
            Guid.NewGuid(),
            assetSymbol,
            assetSymbol + "USDT",
            sampledAtUtc,
            sampledAtUtc,
            priceUsd,
            sampledAtUtc,
            sampledAtUtc,
            "test",
            sampledAtUtc);
    }
}

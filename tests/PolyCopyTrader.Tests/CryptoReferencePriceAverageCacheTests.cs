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
        Assert.Equal(160m, tenMinute.FirstBucketAveragePriceUsd);
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
        Assert.Equal(101m, average.FirstBucketAveragePriceUsd);
        Assert.Equal(now.AddSeconds(-580), average.FirstBucketStartUtc);
        Assert.Equal(now.AddSeconds(10), average.LastBucketStartUtc);
    }

    [Fact]
    public void Reset_SparseTicksWithInternalGapsRemainAvailableInEveryConfiguredWindow()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var cache = new CryptoReferencePriceAverageCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = ["ETH"],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60,
            WindowMinutes = [1440, 720, 360, 180, 90, 45, 20, 10]
        });
        CryptoReferencePriceTick[] ticks =
        [
            Tick("ETH", now.AddMinutes(-9).AddSeconds(-40), 100m),
            Tick("ETH", now.AddMinutes(-5), 200m),
            Tick("ETH", now.AddSeconds(-10), 300m)
        ];

        cache.Reset(ticks, now);

        var averages = cache.GetAssetAverages("ETH");
        Assert.Equal(["24h", "12h", "6h", "3h", "90m", "45m", "20m", "10m"], averages.Select(item => item.WindowLabel));
        Assert.All(averages, average =>
        {
            Assert.True(average.SampleCount > 0);
            Assert.False(average.IsFullWindow);
            Assert.True(average.AveragePriceUsd > 0m);
            Assert.NotNull(average.FirstBucketAveragePriceUsd);
            Assert.NotNull(average.FirstBucketStartUtc);
            Assert.NotNull(average.LastBucketStartUtc);
        });
        var tenMinute = averages.Single(average => average.WindowLabel == "10m");
        Assert.Equal(3, tenMinute.SampleCount);
        Assert.Equal(60, tenMinute.ExpectedSampleCount);
        Assert.Equal(200m, tenMinute.AveragePriceUsd);
    }

    [Fact]
    public void Add_OneShortWindowTickAlsoCreatesAvailableTwentyFourHourDenominator()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var cache = new CryptoReferencePriceAverageCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = ["SOL"],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60,
            WindowMinutes = [1440, 180, 10]
        });
        var tick = Tick("SOL", now.AddMinutes(-5), 150m);

        cache.Add(tick, now);

        var tenMinute = Assert.IsType<CryptoReferencePriceAverage>(cache.GetAverage("SOL", "10m"));
        var twentyFourHour = Assert.IsType<CryptoReferencePriceAverage>(cache.GetAverage("SOL", "24h"));
        Assert.Equal(1, tenMinute.SampleCount);
        Assert.Equal(1, twentyFourHour.SampleCount);
        Assert.Equal(150m, tenMinute.AveragePriceUsd);
        Assert.Equal(150m, twentyFourHour.AveragePriceUsd);
        Assert.Equal(150m, twentyFourHour.FirstBucketAveragePriceUsd);
        Assert.False(tenMinute.IsFullWindow);
        Assert.False(twentyFourHour.IsFullWindow);
    }

    [Fact]
    public void Reset_WithoutTicksLeavesEveryConfiguredWindowUnusable()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var cache = new CryptoReferencePriceAverageCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = ["BTC"],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60,
            WindowMinutes = [1440, 180, 10]
        });

        cache.Reset([], now);

        var averages = cache.GetAssetAverages("BTC");
        Assert.Equal(3, averages.Count);
        Assert.All(averages, average =>
        {
            Assert.Equal(0, average.SampleCount);
            Assert.False(average.IsFullWindow);
            Assert.Null(average.AveragePriceUsd);
            Assert.Null(average.FirstBucketAveragePriceUsd);
            Assert.Null(average.FirstBucketStartUtc);
            Assert.Null(average.LastBucketStartUtc);
        });
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

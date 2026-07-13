using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class CryptoReferencePriceExtremaCacheTests
{
    [Fact]
    public void Reset_BuildsExactExtremaAndAllowsIndividualTickGaps()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 35, TimeSpan.Zero);
        var cache = CreateCache("ETH");
        var ticks = Enumerable.Range(0, 360)
            .Where(index => index != 100)
            .Select(index => Tick(
                "ETH",
                now.AddHours(-1).AddSeconds(5 + index * 10),
                index switch
                {
                    0 => 80m,
                    180 => 130m,
                    _ => 100m
                }))
            .ToArray();

        cache.Reset(ticks, now);

        var extrema = cache.GetExtrema("ETH", 1, now);
        Assert.NotNull(extrema);
        Assert.True(extrema.IsFullWindow);
        Assert.Equal(359, extrema.TickCount);
        Assert.Equal(61, extrema.CoverageBucketCount);
        Assert.Equal(60, extrema.ExpectedCoverageBucketCount);
        Assert.Equal(80m, extrema.MinimumPriceUsd);
        Assert.Equal(ticks[0].SampledAtUtc, extrema.MinimumSampledAtUtc);
        Assert.Equal(130m, extrema.MaximumPriceUsd);
        Assert.Equal(ticks.Single(tick => tick.PriceUsd == 130m).SampledAtUtc, extrema.MaximumSampledAtUtc);
    }

    [Fact]
    public void Add_TrimsOnlyExpiredTicksInsideBoundaryBucket()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 35, TimeSpan.Zero);
        var cache = CreateCache("SOL");
        var ticks = Enumerable.Range(0, 360)
            .Select(index => Tick(
                "SOL",
                now.AddHours(-1).AddSeconds(5 + index * 10),
                index == 0 ? 80m : 100m))
            .ToArray();
        cache.Reset(ticks, now);

        cache.Add(Tick("SOL", now.AddSeconds(20), 105m), now.AddSeconds(20));

        var extrema = cache.GetExtrema("SOL", 1, now.AddSeconds(20));
        Assert.NotNull(extrema);
        Assert.True(extrema.IsFullWindow);
        Assert.Equal(359, extrema.TickCount);
        Assert.Equal(100m, extrema.MinimumPriceUsd);
        Assert.Equal(105m, extrema.MaximumPriceUsd);
    }

    [Fact]
    public void Reset_DoesNotMarkRecentButShortHistoryAsFullWindow()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 35, TimeSpan.Zero);
        var cache = CreateCache("BTC");
        var ticks = Enumerable.Range(0, 356)
            .Select(index => Tick(
                "BTC",
                now.AddHours(-1).AddSeconds(45 + index * 10),
                100m + index))
            .ToArray();

        cache.Reset(ticks, now);

        var extrema = cache.GetExtrema("BTC", 1, now);
        Assert.NotNull(extrema);
        Assert.Equal(60, extrema.CoverageBucketCount);
        Assert.False(extrema.IsFullWindow);
    }

    [Fact]
    public void GetExtrema_ReevaluatesFreshnessAtDecisionTime()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 35, TimeSpan.Zero);
        var cache = CreateCache("BTC");
        var ticks = Enumerable.Range(0, 360)
            .Select(index => Tick(
                "BTC",
                now.AddHours(-1).AddSeconds(5 + index * 10),
                100m + index))
            .ToArray();
        cache.Reset(ticks, now);

        var current = cache.GetExtrema("BTC", 1, now);
        var stale = cache.GetExtrema("BTC", 1, now.AddSeconds(31));

        Assert.NotNull(current);
        Assert.True(current.IsFullWindow);
        Assert.NotNull(stale);
        Assert.False(stale.IsFullWindow);
    }

    private static CryptoReferencePriceExtremaCache CreateCache(string assetSymbol)
    {
        return new CryptoReferencePriceExtremaCache(new CryptoReferencePriceHistoryOptions
        {
            AssetSymbols = [assetSymbol],
            WriteIntervalSeconds = 10,
            TargetSamplesPerWindow = 60
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

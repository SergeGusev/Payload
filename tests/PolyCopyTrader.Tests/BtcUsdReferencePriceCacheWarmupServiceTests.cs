using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.ExternalPrices;

namespace PolyCopyTrader.Tests;

public sealed class BtcUsdReferencePriceCacheWarmupServiceTests
{
    [Fact]
    public async Task StartAsync_LoadsRecentHistoricalSamplesIntoReferenceCache()
    {
        var start = DateTimeOffset.Parse("2026-05-22T10:00:00Z");
        var repository = new TestAppRepository();
        for (var index = 1; index <= 5; index++)
        {
            repository.BtcUpDown5mOddsTicks.Add(CreateTick(index, start.AddMinutes(index)));
        }

        var options = new BinanceBtcUsdReferenceOptions
        {
            Enabled = true,
            WindowSize = 3
        };
        var cache = new BtcUsdReferencePriceCache(options);
        var service = new BtcUsdReferencePriceCacheWarmupService(
            NullLogger<BtcUsdReferencePriceCacheWarmupService>.Instance,
            options,
            cache,
            repository);

        await service.StartAsync(CancellationToken.None);

        var snapshot = cache.Snapshot;
        Assert.Equal(3, snapshot.SampleCount);
        Assert.True(snapshot.IsFullWindow);
        Assert.Equal(4m, snapshot.ArithmeticMeanUsd);
        Assert.Equal([5m, 4m, 3m], snapshot.Samples.Select(sample => sample.PriceUsd));
    }

    private static BtcUpDown5mOddsTick CreateTick(decimal priceUsd, DateTimeOffset sampledAtUtc)
    {
        return new BtcUpDown5mOddsTick(
            Guid.NewGuid(),
            "btc-market",
            "condition",
            "btc-updown-5m-1",
            sampledAtUtc.AddMinutes(-1),
            sampledAtUtc.AddMinutes(4),
            sampledAtUtc,
            60m,
            240m,
            priceUsd,
            sampledAtUtc,
            sampledAtUtc,
            priceUsd,
            0m,
            0m,
            "up",
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            "down",
            null,
            null,
            null,
            null,
            "missing",
            null,
            "missing",
            null,
            "{}",
            sampledAtUtc);
    }
}

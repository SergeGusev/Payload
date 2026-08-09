using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class ActiveMarketAssetSubscriptionRegistryTests
{
    [Fact]
    public async Task AddMarkets_StoresClobTokenIdsAndSignalsChange()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var update = registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-1")]);
        await registry.WaitForChangeAsync(cts.Token);

        Assert.Equal(2, update.Added);
        Assert.Contains("token-yes-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.True(registry.TryGetSnapshot("token-yes-market-1", out var snapshot));
        Assert.Equal("market-1", snapshot.MarketId);
        Assert.Equal("Yes", snapshot.Outcome);
        Assert.Equal(5m, snapshot.OrderMinSize);
        Assert.Equal(0.01m, snapshot.OrderPriceMinTickSize);
        Assert.Equal(0.50m, snapshot.LastTradePrice);
    }

    [Fact]
    public async Task RelevantMarketAssetProvider_IncludesActiveMarketRegistryAssetsWithoutDefaultCap()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets(
            Enumerable.Range(0, 125)
                .Select(index => GammaMarketIngestionTests.CreateMarketForTests("market-" + index))
                .ToArray());
        var repository = new TestAppRepository();
        var provider = new RelevantMarketAssetProvider(
            new MarketDataWebSocketOptions(),
            repository,
            new ExposureSnapshotCache(repository),
            registry);

        var assetIds = await provider.GetRelevantAssetIdsAsync();
        var repeatedAssetIds = await provider.GetRelevantAssetIdsAsync();

        Assert.Equal(250, assetIds.Count);
        Assert.Contains("token-yes-market-124", assetIds, StringComparer.OrdinalIgnoreCase);
        Assert.True(assetIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(repeatedAssetIds));
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
    }

    [Fact]
    public async Task RelevantMarketAssetProvider_ExcludesClosedPaperPositions()
    {
        var repository = new TestAppRepository();
        repository.PaperPositions.Add(new PaperPosition(
            "open-asset",
            "open-condition",
            "Up",
            5m,
            0.50m,
            2.50m,
            0m,
            DateTimeOffset.UtcNow,
            "open-wallet"));
        repository.PaperPositions.Add(new PaperPosition(
            "closed-asset",
            "closed-condition",
            "Down",
            0m,
            0.50m,
            0m,
            0m,
            DateTimeOffset.UtcNow,
            "closed-wallet"));
        var provider = new RelevantMarketAssetProvider(
            new MarketDataWebSocketOptions(),
            repository,
            new ExposureSnapshotCache(repository),
            new ActiveMarketAssetSubscriptionRegistry());

        var assetIds = await provider.GetRelevantAssetIdsAsync();
        var repeatedAssetIds = await provider.GetRelevantAssetIdsAsync();

        Assert.Contains("open-asset", assetIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("closed-asset", assetIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("open-asset", repeatedAssetIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("closed-asset", repeatedAssetIds, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
        Assert.Equal(0, repository.GetPaperPositionsCalls);
    }

    [Fact]
    public async Task RelevantMarketAssetProvider_PrioritizesNewestCachedPositionWhenCapped()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PaperPositions.Add(new PaperPosition(
            "older-asset",
            "older-condition",
            "Up",
            5m,
            0.50m,
            2.50m,
            0m,
            now.AddMinutes(-1),
            "older-wallet"));
        repository.PaperPositions.Add(new PaperPosition(
            "newer-asset",
            "newer-condition",
            "Down",
            5m,
            0.50m,
            2.50m,
            0m,
            now,
            "newer-wallet"));
        var provider = new RelevantMarketAssetProvider(
            new MarketDataWebSocketOptions { MaxSubscribedAssets = 1 },
            repository,
            new ExposureSnapshotCache(repository),
            new ActiveMarketAssetSubscriptionRegistry());

        var assetIds = await provider.GetRelevantAssetIdsAsync();

        Assert.Equal(["newer-asset"], assetIds);
        Assert.Equal(1, repository.GetOpenPaperPositionsCalls);
    }

    [Fact]
    public async Task RetainAssets_RemovesAssetsMissingFromFullScanAndSignalsChange()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets(
        [
            GammaMarketIngestionTests.CreateMarketForTests("market-1"),
            GammaMarketIngestionTests.CreateMarketForTests("market-2")
        ]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await registry.WaitForChangeAsync(cts.Token);

        var update = registry.RetainAssets(["token-yes-market-2", "token-no-market-2"]);
        await registry.WaitForChangeAsync(cts.Token);

        Assert.Equal(2, update.Removed);
        Assert.DoesNotContain("token-yes-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-yes-market-2", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void RetainAssets_PreservesDayAheadAssetsProtectedUntilMarketEnd()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        var market = GammaMarketIngestionTests.CreateMarketForTests("day-ahead") with
        {
            EndDateUtc = DateTimeOffset.UtcNow.AddHours(24)
        };
        registry.AddOrUpdateMarkets([market], protectFromFullScanRetention: true);

        var update = registry.RetainAssets([]);

        Assert.Equal(0, update.Removed);
        Assert.Contains("token-yes-day-ahead", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("token-no-day-ahead", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyMarketDataUpdate_UpdatesDecisionPricesInSnapshot()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-1")]);

        var updated = registry.ApplyMarketDataUpdate(new MarketDataUpdate(
            MarketDataEventType.BestBidAsk,
            "best_bid_ask",
            "token-yes-market-1",
            "condition-market-1",
            null,
            0.47m,
            0.49m,
            null,
            null,
            TradeSide.Unknown,
            false,
            DateTimeOffset.UtcNow.AddSeconds(1)));

        Assert.True(updated);
        Assert.True(registry.TryGetSnapshot("token-yes-market-1", out var snapshot));
        Assert.Equal(0.47m, snapshot.BestBid);
        Assert.Equal(0.49m, snapshot.BestAsk);
        Assert.Equal(0.02m, snapshot.Spread);
    }

    [Fact]
    public async Task ApplyMarketDataUpdate_RemovesResolvedAssetAndSignalsChange()
    {
        var registry = new ActiveMarketAssetSubscriptionRegistry();
        registry.AddOrUpdateMarkets([GammaMarketIngestionTests.CreateMarketForTests("market-1")]);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await registry.WaitForChangeAsync(cts.Token);

        var updated = registry.ApplyMarketDataUpdate(new MarketDataUpdate(
            MarketDataEventType.MarketResolved,
            "market_resolved",
            "token-yes-market-1",
            "condition-market-1",
            null,
            null,
            null,
            null,
            null,
            TradeSide.Unknown,
            true,
            DateTimeOffset.UtcNow.AddSeconds(1)));
        await registry.WaitForChangeAsync(cts.Token);

        Assert.True(updated);
        Assert.DoesNotContain("token-yes-market-1", registry.GetAssetIds(), StringComparer.OrdinalIgnoreCase);
        Assert.False(registry.TryGetSnapshot("token-yes-market-1", out _));
    }
}

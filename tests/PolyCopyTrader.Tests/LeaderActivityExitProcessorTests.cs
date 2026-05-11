using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class LeaderActivityExitProcessorTests
{
    private const string Wallet = "0x1111111111111111111111111111111111111111";
    private const string AssetId = "token-yes";
    private const string ConditionId = "condition-1";

    [Fact]
    public async Task ProcessOnce_CreatesProportionalSellOrderFromLeaderActivity()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PaperCopiedLeaderPositions.Add(CopiedLeaderPosition(now.AddSeconds(-10), 1_000m, 20m));
        repository.PaperPositions.Add(PaperPosition(20m));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([SellActivity(now.AddSeconds(-1), 250m, 0.37m, "0xsell1")]));

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(1, result.PositionsSelected);
        Assert.Equal(1, result.WalletsChecked);
        Assert.Equal(1, result.ActivityRowsFetched);
        Assert.Equal(1, result.SellEventsMatched);
        Assert.Equal(1, result.ExitOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(Wallet, order.CopiedTraderWallet);
        Assert.Equal(TradeSide.Sell, order.Side);
        Assert.Equal(AssetId, order.AssetId);
        Assert.Equal(0.37m, order.Price);
        Assert.Equal(5m, order.SizeShares);
        var signal = Assert.Single(repository.Signals);
        Assert.True(signal.Accepted);
        Assert.Equal("paper_leader_activity_partial_exit", signal.DecisionCode);
        Assert.Equal(0.37m, signal.ProposedPaperPrice);
        Assert.Equal(0.37m, signal.LeaderTrade.Price);
        var copiedPosition = Assert.Single(repository.PaperCopiedLeaderPositions);
        Assert.Equal(PaperCopiedLeaderPositionStatus.Active, copiedPosition.Status);
        Assert.Equal(250m, copiedPosition.LeaderSoldSizeShares);
        Assert.Equal(5m, copiedPosition.CopiedExitRequestedSizeShares);
        Assert.Equal("0xsell1", copiedPosition.LastActivityTransactionHash);
        var activityEvent = Assert.Single(repository.PaperCopiedLeaderActivityEvents);
        Assert.Equal(Wallet, activityEvent.CopiedTraderWallet);
        Assert.Equal(TradeSide.Sell, activityEvent.Side);
    }

    [Fact]
    public async Task ProcessOnce_CapsExitOrderByAvailablePaperPosition()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PaperCopiedLeaderPositions.Add(CopiedLeaderPosition(now.AddSeconds(-10), 1_000m, 20m));
        repository.PaperPositions.Add(PaperPosition(6m));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([SellActivity(now.AddSeconds(-1), 500m, 0.42m, "0xsell2")]));

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(1, result.ExitOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(6m, order.SizeShares);
        var copiedPosition = Assert.Single(repository.PaperCopiedLeaderPositions);
        Assert.Equal(300m, copiedPosition.LeaderSoldSizeShares);
        Assert.Equal(6m, copiedPosition.CopiedExitRequestedSizeShares);
    }

    [Fact]
    public async Task ProcessOnce_SkipsExitOrderWhenLeaderActivityPriceIsInvalid()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new TestAppRepository();
        repository.PaperCopiedLeaderPositions.Add(CopiedLeaderPosition(now.AddSeconds(-10), 1_000m, 20m));
        repository.PaperPositions.Add(PaperPosition(20m));
        var processor = CreateProcessor(
            repository,
            new FakeDataApiClient([SellActivity(now.AddSeconds(-1), 250m, 0m, "0xsell-invalid-price")]));

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(1, result.ActivityRowsFetched);
        Assert.Equal(0, result.SellEventsMatched);
        Assert.Equal(0, result.ExitOrdersCreated);
        Assert.Empty(repository.PaperOrders);
        Assert.Empty(repository.Signals);
        Assert.Empty(repository.PaperCopiedLeaderActivityEvents);
    }

    [Fact]
    public async Task ProcessOnce_DoesNotCreateDuplicateOrderForSameActivityEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var activity = SellActivity(now.AddSeconds(-1), 250m, 0.40m, "0xsell3");
        var repository = new TestAppRepository();
        repository.PaperCopiedLeaderPositions.Add(CopiedLeaderPosition(now.AddSeconds(-10), 1_000m, 20m));
        repository.PaperPositions.Add(PaperPosition(20m));
        var processor = CreateProcessor(repository, new FakeDataApiClient([activity]));

        await processor.ProcessOnceAsync();
        var activePosition = repository.PaperCopiedLeaderPositions.Single();
        repository.PaperCopiedLeaderPositions.Clear();
        repository.PaperCopiedLeaderPositions.Add(activePosition with
        {
            NextActivitySyncAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
        });
        var duplicateResult = await processor.ProcessOnceAsync();

        Assert.Equal(1, duplicateResult.DuplicateEvents);
        Assert.Single(repository.PaperOrders);
        Assert.Single(repository.PaperCopiedLeaderActivityEvents);
    }

    private static LeaderActivityExitProcessor CreateProcessor(
        TestAppRepository repository,
        IPolymarketDataApiClient dataApiClient)
    {
        return new LeaderActivityExitProcessor(
            NullLogger<LeaderActivityExitProcessor>.Instance,
            new PaperTradingOptions
            {
                DefaultOrderTtlSeconds = 300,
                LeaderActivityExitTrackingEnabled = true,
                LeaderActivityExitTrackingBatchSize = 100,
                LeaderActivityExitTrackingActivityLimit = 500,
                LeaderActivityExitTrackingPollDelayMilliseconds = 1_000
            },
            dataApiClient,
            new DefaultPaperTradingEngine(),
            new ExposureSnapshotCache(repository),
            repository);
    }

    private static PaperCopiedLeaderPosition CopiedLeaderPosition(
        DateTimeOffset entryTimestampUtc,
        decimal leaderInitialSizeShares,
        decimal copiedInitialSizeShares)
    {
        return new PaperCopiedLeaderPosition(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Wallet,
            AssetId,
            ConditionId,
            "Yes",
            "0xentry",
            entryTimestampUtc,
            0.50m,
            leaderInitialSizeShares,
            copiedInitialSizeShares,
            LeaderSoldSizeShares: 0m,
            CopiedExitRequestedSizeShares: 0m,
            PaperCopiedLeaderPositionStatus.Active,
            LastActivityTimestampUtc: null,
            LastActivityTransactionHash: null,
            LastActivitySyncAtUtc: null,
            NextActivitySyncAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
            CreatedAtUtc: entryTimestampUtc,
            UpdatedAtUtc: entryTimestampUtc);
    }

    private static PaperPosition PaperPosition(decimal sizeShares)
    {
        return new PaperPosition(
            AssetId,
            ConditionId,
            "Yes",
            sizeShares,
            0.50m,
            sizeShares * 0.40m,
            0m,
            DateTimeOffset.UtcNow,
            Wallet);
    }

    private static PolymarketDataApiActivity SellActivity(
        DateTimeOffset timestampUtc,
        decimal sizeShares,
        decimal price,
        string transactionHash)
    {
        return new PolymarketDataApiActivity(
            Wallet,
            timestampUtc,
            ConditionId,
            PolymarketDataApiActivityType.Trade,
            sizeShares,
            sizeShares * price,
            transactionHash,
            price,
            AssetId,
            TradeSide.Sell,
            OutcomeIndex: 0,
            "Market title",
            "market-slug",
            Icon: null,
            EventSlug: null,
            "Yes",
            "Leader",
            Pseudonym: null,
            Bio: null,
            ProfileImage: null,
            ProfileImageOptimized: null,
            "{}");
    }

    private sealed class FakeDataApiClient(IReadOnlyList<PolymarketDataApiActivity> activities) : IPolymarketDataApiClient
    {
        public Task<IReadOnlyList<TraderLeaderboardEntry>> GetTraderLeaderboardAsync(
            string category = "OVERALL",
            string timePeriod = "DAY",
            string orderBy = "PNL",
            int limit = 25,
            int offset = 0,
            string? user = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TraderLeaderboardEntry>>([]);
        }

        public Task<IReadOnlyList<LeaderTrade>> GetUserTradesAsync(
            string wallet,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<PolymarketDataApiActivity>> GetUserActivityAsync(
            string wallet,
            int limit = 500,
            int offset = 0,
            string sortBy = "TIMESTAMP",
            string sortDirection = "DESC",
            long? timestampCacheBuster = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PolymarketDataApiActivity>>(
                activities
                    .Where(activity => string.Equals(activity.Wallet, wallet, StringComparison.OrdinalIgnoreCase))
                    .Take(limit)
                    .ToArray());
        }

        public Task<IReadOnlyList<LeaderTrade>> GetMarketTradesAsync(
            string conditionId,
            bool takerOnly,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderTrade>>([]);
        }

        public Task<IReadOnlyList<LeaderPosition>> GetUserPositionsAsync(
            string wallet,
            int limit = 100,
            int offset = 0,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<LeaderPosition>>([]);
        }
    }

}

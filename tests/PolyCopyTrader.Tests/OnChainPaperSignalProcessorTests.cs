using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Service.MarketData;
using PolyCopyTrader.Service.OnChain;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class OnChainPaperSignalProcessorTests
{
    private const string MakerWallet = "0x1111111111111111111111111111111111111111";
    private const string TakerWallet = "0x2222222222222222222222222222222222222222";
    private const string ThirdWallet = "0x3333333333333333333333333333333333333333";
    private const string TokenYes = "token-yes";
    private const string TokenNo = "token-no";

    [Fact]
    public async Task ProcessOnce_CreatesPaperOrderForRatedBuyMaker()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Buy, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));

        var processor = CreateProcessor(repository);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Single(repository.PaperOrders);
        var order = repository.PaperOrders.Single();
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
        Assert.Equal(TokenYes, order.AssetId);
        Assert.Equal(TradeSide.Buy, order.Side);
        var copiedPosition = Assert.Single(repository.PaperCopiedLeaderPositions);
        Assert.Equal(PaperCopiedLeaderPositionStatus.PendingEntry, copiedPosition.Status);
        Assert.Equal(order.Id, copiedPosition.EntryPaperOrderId);
        Assert.Equal(2_000m, copiedPosition.LeaderInitialSizeShares);
        Assert.Single(repository.Signals, signal => signal.Accepted);
        var processingResult = Assert.Single(repository.OnChainPaperSignalResults, item => item.Status == "PaperOrderCreated");
        Assert.Equal(MakerWallet, processingResult.CopiedTraderWallet);
        Assert.Equal(order.Id, processingResult.PaperOrderId);
    }

    [Fact]
    public async Task ProcessCaptures_CreatesPaperOrderForFreshRatedBuyMakerWithoutCaptureBacklog()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Empty(repository.PolymarketOnChainTradeCaptures);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
    }

    [Fact]
    public async Task ProcessCaptures_DoesNotCreatePaperOrderWhenFollowLeaderStrategyIsDisabled()
    {
        var repository = new TestAppRepository();
        repository.StrategyEnabledStates[StrategyIds.FollowLeader] = false;
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(0, result.CandidatesFetched);
        Assert.Equal(0, result.PaperOrdersCreated);
        Assert.Empty(repository.Signals);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessCaptures_CreatesPaperOrderInLiveModeWhenPaperRunInLiveModeIsEnabled()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var processor = CreateProcessor(
            repository,
            botOptions: new BotOptions { Mode = BotMode.Live },
            paperOptionsOverride: new PaperTradingOptions
            {
                InitialBankrollUsd = 10_000m,
                DefaultOrderTtlSeconds = 300,
                RunInLiveMode = true
            });

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(1, result.PaperOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
    }

    [Fact]
    public async Task ProcessCaptures_SelectsBestRatedBuyCandidateFromLatestWindow()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(ThirdWallet, pnl: 10m, ratio: 1m));
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet, pnl: 100m, ratio: 10m));
        var processor = CreateProcessor(repository, hotMaxAgeSeconds: 60);

        var result = await processor.ProcessCapturesAsync(
            [
                Capture(TradeSide.Buy, ThirdWallet, TakerWallet, timestampOffsetSeconds: -2),
                Capture(TradeSide.Buy, MakerWallet, TakerWallet, timestampOffsetSeconds: -1)
            ]);

        Assert.Equal(4, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
        Assert.DoesNotContain(repository.PaperOrders, item => item.CopiedTraderWallet == ThirdWallet);
    }

    [Fact]
    public async Task ProcessCaptures_OnlyConsidersLatestConfiguredCandidateWindow()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet, pnl: 100m, ratio: 10m));
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(ThirdWallet, pnl: 10m, ratio: 1m));
        var processor = CreateProcessor(repository, hotMaxAgeSeconds: 60, latestCandidatesLimit: 1);

        var result = await processor.ProcessCapturesAsync(
            [
                Capture(TradeSide.Buy, MakerWallet, TakerWallet, timestampOffsetSeconds: -2),
                Capture(TradeSide.Buy, ThirdWallet, TakerWallet, timestampOffsetSeconds: -1)
            ]);

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(ThirdWallet, order.CopiedTraderWallet);
    }

    [Fact]
    public async Task ProcessCaptures_FetchesClobBookWhenFreshCachedOrderBookIsMissing()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var clobClient = new FakeClobClient(OrderBook());
        var processor = CreateProcessor(repository, seedOrderBookCache: false, fakeClobClient: clobClient);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Equal(1, clobClient.OrderBookRequests);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
        Assert.Equal(TokenYes, order.AssetId);
        Assert.DoesNotContain(repository.OnChainPaperSignalResults, item =>
            item.DecisionCode == SignalReasonCodes.MissingOrderBookCacheMiss);
    }

    [Fact]
    public async Task ProcessCaptures_RejectsWithRestMissingWhenFreshCachedAndClobOrderBooksAreMissing()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var clobClient = new FakeClobClient(null);
        var processor = CreateProcessor(repository, seedOrderBookCache: false, fakeClobClient: clobClient);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(0, result.PaperOrdersCreated);
        Assert.Equal(1, clobClient.OrderBookRequests);
        Assert.Empty(repository.PaperOrders);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.Status == "Rejected" &&
            item.DecisionCode == SignalReasonCodes.MissingOrderBookRestMissing);
    }

    [Fact]
    public async Task ProcessCaptures_UsesClobFallbackForBestCandidateBeforeTryingNext()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet, pnl: 100m, ratio: 10m));
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(ThirdWallet, pnl: 10m, ratio: 1m));
        var clobClient = new FakeClobClient(OrderBook(TokenYes));
        var processor = CreateProcessor(
            repository,
            hotMaxAgeSeconds: 60,
            seedOrderBookCache: false,
            fakeClobClient: clobClient,
            cachedOrderBooks: [OrderBook(TokenNo)]);

        var result = await processor.ProcessCapturesAsync(
            [
                Capture(TradeSide.Buy, ThirdWallet, TakerWallet, tokenId: TokenNo, timestampOffsetSeconds: -2),
                Capture(TradeSide.Buy, MakerWallet, TakerWallet, tokenId: TokenYes, timestampOffsetSeconds: -1)
            ]);

        Assert.Equal(4, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Equal(1, clobClient.OrderBookRequests);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(MakerWallet, order.CopiedTraderWallet);
        Assert.Equal(TokenYes, order.AssetId);
        Assert.DoesNotContain(repository.OnChainPaperSignalResults, item =>
            item.DecisionCode == SignalReasonCodes.MissingOrderBookCacheMiss);
    }

    [Fact]
    public async Task ProcessCaptures_FetchesClobBookWhenCachedOrderBookIsStale()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var clobClient = new FakeClobClient(OrderBook(TokenYes));
        var processor = CreateProcessor(
            repository,
            fakeClobClient: clobClient,
            cachedOrderBooks: [OrderBook(TokenYes, DateTimeOffset.UtcNow.AddMinutes(-2))]);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet)]);

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.Equal(1, clobClient.OrderBookRequests);
        Assert.DoesNotContain(repository.OnChainPaperSignalResults, item =>
            item.DecisionCode == SignalReasonCodes.MissingOrderBookCacheStale);
    }

    [Fact]
    public async Task ProcessOnce_RecordsRestNotFoundOrderBookSubreason()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Buy, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var clobClient = new FakeClobClient(
            null,
            new PolymarketApiException("test", "GetOrderBook", "No orderbook exists for the requested token id"));
        var processor = CreateProcessor(repository, seedOrderBookCache: false, fakeClobClient: clobClient);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(0, result.PaperOrdersCreated);
        Assert.Equal(1, clobClient.OrderBookRequests);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.Status == "Rejected" &&
            item.DecisionCode == SignalReasonCodes.MissingOrderBookRestNotFound);
    }

    [Fact]
    public async Task ProcessCaptures_IgnoresStaleCapturesBeforeCandidateLookup()
    {
        var repository = new TestAppRepository();
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        var processor = CreateProcessor(repository);

        var result = await processor.ProcessCapturesAsync(
            [Capture(TradeSide.Buy, MakerWallet, TakerWallet, timestampOffsetSeconds: -30)]);

        Assert.Equal(0, result.CandidatesFetched);
        Assert.Empty(repository.Signals);
        Assert.Empty(repository.PaperOrders);
    }

    [Fact]
    public async Task ProcessOnce_DoesNotRejectRestrictedMarketWhenOtherwiseEligible()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Buy, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market(restricted: true));
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));

        var processor = CreateProcessor(repository);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        Assert.DoesNotContain(repository.SignalRejections, rejection =>
            rejection.ReasonCode is SignalReasonCodes.MarketInactive or SignalReasonCodes.MarketRestricted);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.Status == "PaperOrderCreated" &&
            item.CopiedTraderWallet == MakerWallet);
    }

    [Fact]
    public async Task ProcessOnce_CopiesTakerWalletWhenTakerIsBuySide()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Sell, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(TakerWallet));

        var processor = CreateProcessor(repository);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(1, result.PaperOrdersCreated);
        var order = Assert.Single(repository.PaperOrders);
        Assert.Equal(TakerWallet, order.CopiedTraderWallet);
        Assert.Equal(TradeSide.Buy, order.Side);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.ParticipantRole == OnChainParticipantRole.Taker &&
            item.Status == "PaperOrderCreated");
    }

    [Fact]
    public async Task ProcessOnce_IgnoresSellPaperOrderWhenCopiedWalletHasPosition()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Sell, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));
        repository.PaperPositions.Add(new PaperPosition(
            TokenYes,
            "condition-1",
            "Yes",
            100m,
            0.40m,
            49m,
            9m,
            DateTimeOffset.UtcNow,
            MakerWallet));

        var processor = CreateProcessor(repository);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(0, result.PaperOrdersCreated);
        Assert.Empty(repository.PaperOrders);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.ParticipantRole == OnChainParticipantRole.Maker &&
            item.Status == "Ignored" &&
            item.DecisionCode == "onchain_sell_ignored");
    }

    [Fact]
    public async Task ProcessOnce_RejectsWhenPolymarketRatingIsMissing()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Buy, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());

        var processor = CreateProcessor(repository);

        var result = await processor.ProcessOnceAsync();

        Assert.Equal(2, result.CandidatesFetched);
        Assert.Equal(0, result.PaperOrdersCreated);
        Assert.Empty(repository.PaperOrders);
        Assert.Contains(repository.SignalRejections, rejection =>
            rejection.ReasonCode == SignalReasonCodes.MissingPolymarketRating);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.ParticipantRole == OnChainParticipantRole.Maker &&
            item.Status == "Rejected" &&
            item.DecisionCode == SignalReasonCodes.MissingPolymarketRating);
        Assert.Contains(repository.OnChainPaperSignalResults, item =>
            item.ParticipantRole == OnChainParticipantRole.Taker &&
            item.Status == "Ignored" &&
            item.DecisionCode == "onchain_sell_ignored");
    }

    [Fact]
    public async Task ProcessOnce_DeduplicatesProcessedParticipants()
    {
        var repository = new TestAppRepository();
        repository.PolymarketOnChainTradeCaptures.Add(Capture(TradeSide.Buy, MakerWallet, TakerWallet));
        repository.PolymarketGammaMarkets.Add(Market());
        repository.PolymarketDataApiWalletCategoryRatings.Add(Rating(MakerWallet));

        var processor = CreateProcessor(repository);

        await processor.ProcessOnceAsync();
        var result = await processor.ProcessOnceAsync();

        Assert.Equal(0, result.CandidatesFetched);
        Assert.Equal(2, repository.OnChainPaperSignalResults.Count);
        Assert.Single(repository.PaperOrders);
    }

    private static OnChainPaperSignalProcessor CreateProcessor(
        TestAppRepository repository,
        int hotMaxAgeSeconds = 2,
        int latestCandidatesLimit = 100,
        bool seedOrderBookCache = true,
        FakeClobClient? fakeClobClient = null,
        IReadOnlyList<OrderBookSnapshot>? cachedOrderBooks = null,
        BotOptions? botOptions = null,
        PaperTradingOptions? paperOptionsOverride = null)
    {
        var riskOptions = new RiskOptions();
        var paperOptions = paperOptionsOverride ??
            new PaperTradingOptions { InitialBankrollUsd = 10_000m, DefaultOrderTtlSeconds = 300 };
        var signalOptions = new SignalOptions
        {
            RequireKnownMarketCategory = true,
            RequireLeaderCategoryPerformance = true,
            MinLeaderCategoryResolvedPositions = 3,
            MinLeaderCategoryResolvedRoiPct = 0m,
            MinLeaderCategoryWinRatePct = 50m,
            MinLeaderCategoryScore = 0m,
            MinLeaderCategorySampleQuality = "Low"
        };

        var marketDataOptions = new MarketDataWebSocketOptions();
        var marketDataCache = new MarketDataCache(marketDataOptions);
        marketDataCache.ReplaceSubscribedAssets([TokenYes, TokenNo]);
        var orderBook = OrderBook();
        IReadOnlyList<OrderBookSnapshot> seedOrderBooks = cachedOrderBooks ??
            (seedOrderBookCache ? [orderBook] : Array.Empty<OrderBookSnapshot>());
        foreach (var cachedOrderBook in seedOrderBooks)
        {
            marketDataCache.ApplyUpdate(new MarketDataUpdate(
                MarketDataEventType.Book,
                "book",
                cachedOrderBook.AssetId,
                cachedOrderBook.ConditionId,
                cachedOrderBook,
                cachedOrderBook.BestBid,
                cachedOrderBook.BestAsk,
                null,
                null,
                TradeSide.Unknown,
                MarketResolved: false,
                DateTimeOffset.UtcNow));
        }

        return new OnChainPaperSignalProcessor(
            NullLogger<OnChainPaperSignalProcessor>.Instance,
            botOptions ?? new BotOptions { Mode = BotMode.Paper },
            new OnChainIngestionOptions
            {
                PaperSignalEnabled = true,
                PaperSignalBatchSize = 10,
                PaperSignalMaxLagSeconds = 300,
                PaperSignalHotMaxAgeSeconds = hotMaxAgeSeconds,
                PaperSignalLatestCandidatesLimit = latestCandidatesLimit,
                PaperSignalRatingStaleAfterHours = 24,
                PaperSignalRequirePolymarketRatingFound = true,
                PaperSignalMinLeaderboardPnlUsd = 0m,
                PaperSignalMinLeaderboardPnlToVolumePct = 0m
            },
            new DataApiTraderIngestionOptions
            {
                PolymarketRatingTimePeriod = "ALL",
                PolymarketRatingOrderBy = "PNL"
            },
            new ExecutionOptions
            {
                MinLeaderTradeUsd = 500m,
                MaxSlippageCents = 1m,
                MaxSpreadCents = 2m,
                MaxSpreadPct = 5m
            },
            signalOptions,
            paperOptions,
            marketDataOptions,
            fakeClobClient ?? new FakeClobClient(orderBook),
            marketDataCache,
            new ExposureSnapshotCache(repository),
            new DefaultSignalEngine(
                signalOptions,
                new ExecutionOptions
                {
                    MinLeaderTradeUsd = 500m,
                    MaxSlippageCents = 1m,
                    MaxSpreadCents = 2m,
                    MaxSpreadPct = 5m
                },
                riskOptions,
                paperOptions,
                new DefaultRiskEngine(riskOptions, paperOptions)),
            new DefaultPaperTradingEngine(),
            new StrategyStateProvider(NullLogger<StrategyStateProvider>.Instance, repository),
            repository);
    }

    private static PolymarketOnChainTradeCapture Capture(
        TradeSide side,
        string maker,
        string taker,
        string tokenId = TokenYes,
        int timestampOffsetSeconds = -1)
    {
        return new PolymarketOnChainTradeCapture(
            Guid.NewGuid(),
            "CTF Exchange V2",
            "0xcontract",
            "V2",
            100,
            DateTimeOffset.UtcNow.AddSeconds(timestampOffsetSeconds),
            "0xblock",
            "0x" + Guid.NewGuid().ToString("N"),
            0,
            1,
            "0xorder",
            maker,
            taker,
            maker,
            side,
            tokenId,
            "0",
            tokenId,
            "500000",
            "1000000",
            0.5m,
            1m,
            0.5m,
            2_000m,
            1_000m,
            "0",
            0m,
            "0",
            null,
            null,
            ["0xtopic"],
            "0xdata",
            Removed: false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private static PolymarketGammaMarket Market(bool restricted = false)
    {
        return new PolymarketGammaMarket(
            "market-1",
            "condition-1",
            "question-1",
            "sample-market",
            "Will sample event happen?",
            "event-1",
            "event-slug",
            "Event title",
            "series",
            "Politics",
            Active: true,
            Closed: false,
            Archived: false,
            Restricted: restricted,
            AcceptingOrders: true,
            EnableOrderBook: true,
            NegativeRisk: false,
            Liquidity: 10_000m,
            LiquidityClob: 10_000m,
            Volume: 100_000m,
            Volume24Hr: 1_000m,
            BestBid: 0.49m,
            BestAsk: 0.51m,
            Spread: 0.02m,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            StartDateUtc: DateTimeOffset.UtcNow.AddDays(-1),
            EndDateUtc: DateTimeOffset.UtcNow.AddDays(7),
            EventStartTimeUtc: DateTimeOffset.UtcNow.AddDays(7),
            Outcomes: ["Yes", "No"],
            ClobTokenIds: [TokenYes, TokenNo],
            RawJson: "{}",
            FetchedAtUtc: DateTimeOffset.UtcNow,
            LastTradePrice: 0.5m,
            OrderMinSize: 1m,
            OrderPriceMinTickSize: 0.01m);
    }

    private static PolymarketDataApiWalletCategoryRating Rating(
        string wallet,
        decimal pnl = 100m,
        decimal ratio = 10m)
    {
        return new PolymarketDataApiWalletCategoryRating(
            wallet,
            "Politics",
            "POLITICS",
            "ALL",
            "PNL",
            Found: true,
            Rank: 10,
            UserName: "Leader",
            XUsername: null,
            ProfileImage: null,
            VerifiedBadge: false,
            LeaderboardPnlUsd: pnl,
            LeaderboardVolumeUsd: 1_000m,
            LeaderboardPnlToVolumePct: ratio,
            RefreshedAtUtc: DateTimeOffset.UtcNow,
            RawJson: "{}",
            CurrentPositionsCount: 5,
            ClosedPositionsCount: 5,
            PositionsTotalPnlUsd: 100m,
            PositionsTotalPercentPnl: 10m,
            PositionsRefreshedAtUtc: DateTimeOffset.UtcNow);
    }

    private static OrderBookSnapshot OrderBook(
        string assetId = TokenYes,
        DateTimeOffset? snapshotAtUtc = null)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(0.49m, 1_000m)],
            [new OrderBookLevel(0.51m, 1_000m)],
            snapshotAtUtc ?? DateTimeOffset.UtcNow,
            "condition-1",
            TickSize: 0.01m);
    }

    private sealed class FakeClobClient(
        OrderBookSnapshot? orderBook,
        Exception? exception = null) : IPolymarketClobPublicClient
    {
        public int OrderBookRequests { get; private set; }

        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default)
        {
            OrderBookRequests++;
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(orderBook);
        }

        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<decimal?>(null);
        }

        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PolymarketClobMarketByToken?>(null);
        }
    }
}

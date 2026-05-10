using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class StrategyEngineTests
{
    private const string Wallet = "0x56687bf447db6ffa42ffe2204a05edaa20f55839";

    [Fact]
    public void MakerPriceFormula_UsesBidTickMaxEntryAndAskTick()
    {
        var proposed = MakerPriceCalculator.Calculate(
            bestBid: 0.73m,
            bestAsk: 0.76m,
            tickSize: 0.01m,
            maxEntry: 0.75m);

        Assert.Equal(0.74m, proposed);
    }

    [Fact]
    public void MakerPriceFormula_SellUsesAskTickMinExitAndBidTick()
    {
        var proposed = MakerPriceCalculator.CalculateSell(
            bestBid: 0.73m,
            bestAsk: 0.76m,
            tickSize: 0.01m,
            minExit: 0.74m);

        Assert.Equal(0.75m, proposed);
    }

    [Fact]
    public void SignalEngine_AcceptsBuySignalAtLeaderPrice()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m)));

        Assert.True(decision.Accepted);
        Assert.Equal("paper_order_small", decision.DecisionCode);
        Assert.Equal(0.74m, decision.ProposedPrice);
        Assert.True(decision.Score >= 75);
    }

    [Fact]
    public void SignalEngine_RejectsMissingMarketCategoryWhenRequired()
    {
        var engine = CreateSignalEngine(StrictCategorySignalOptions());

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            marketInfo: new MarketInfo("condition-1", "sample-market", "Will sample event happen?", null, DateTimeOffset.UtcNow.AddDays(2))));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.MissingMarketCategory, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsMissingLeaderCategoryPerformanceWhenRequired()
    {
        var engine = CreateSignalEngine(StrictCategorySignalOptions());

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.MissingLeaderCategoryPerformance, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsWeakLeaderCategoryPerformance()
    {
        var engine = CreateSignalEngine(StrictCategorySignalOptions());

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            leaderCategoryPerformance: GoodCategoryPerformance() with
            {
                ResolvedPositions = 1,
                SampleQuality = "Thin"
            }));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.LeaderCategoryResolvedSampleTooSmall, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_AcceptsWhenLeaderCategoryPerformancePasses()
    {
        var engine = CreateSignalEngine(StrictCategorySignalOptions());

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            leaderCategoryPerformance: GoodCategoryPerformance()));

        Assert.True(decision.Accepted);
        Assert.True(decision.Score >= 90);
    }

    [Fact]
    public void SignalEngine_RejectsWeakCopiedTraderOverallPerformance()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            copiedTraderOverallPerformance: CopiedPerformance("OVERALL", settledPositionsCount: 3, totalPnlUsd: -3m, roiPct: -15m, score: 30m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.CopiedTraderPerformanceTooWeak, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsWeakCopiedTraderCategoryPerformance()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            copiedTraderCategoryPerformance: CopiedPerformance("POLITICS", settledPositionsCount: 3, totalPnlUsd: -3m, roiPct: -15m, score: 30m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.CopiedTraderCategoryPerformanceTooWeak, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_IgnoresThinCopiedTraderPerformance()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            copiedTraderOverallPerformance: CopiedPerformance("OVERALL", settledPositionsCount: 2, totalPnlUsd: -50m, roiPct: -80m, score: 0m)));

        Assert.True(decision.Accepted);
    }

    [Fact]
    public void SignalEngine_AddsCopiedTraderPerformanceScoreForHealthyPerformance()
    {
        var engine = CreateSignalEngine(new SignalOptions { NormalPaperOrderScore = 200 });
        var trade = Trade();
        var orderBook = OrderBook(0.735m, 0.745m);

        var baseline = engine.Evaluate(CreateContext(orderBook, trade: trade));
        var decision = engine.Evaluate(CreateContext(
            orderBook,
            trade: trade,
            copiedTraderOverallPerformance: CopiedPerformance("OVERALL", settledPositionsCount: 5, totalPnlUsd: 4m, roiPct: 12m, score: 70m)));

        Assert.True(baseline.Accepted);
        Assert.True(decision.Accepted);
        Assert.Equal(baseline.Score + 10, decision.Score);
    }

    [Fact]
    public void SignalEngine_RejectsDisallowedCategory()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            marketInfo: new MarketInfo("condition-1", "sample-market", "Will sample event happen?", "SPORTS", DateTimeOffset.UtcNow.AddDays(2))));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.CategoryNotAllowed, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsStaleSignal()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            trade: Trade() with { TimestampUtc = DateTimeOffset.UtcNow.AddMinutes(-10) }));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.TradeTooOld, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_AcceptsSellSignalWhenCopiedPositionExists()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            trade: Trade() with { Side = TradeSide.Sell },
            availablePositionSizeShares: 20m));

        Assert.True(decision.Accepted);
        Assert.Equal(0.74m, decision.ProposedPrice);
        Assert.True(decision.ProposedSizeShares <= 20m);
    }

    [Fact]
    public void SignalEngine_UsesMarketMinimumOrderSizeWhenConfigured()
    {
        var engine = CreateSignalEngine(
            paperOptions: new PaperTradingOptions
            {
                InitialBankrollUsd = 10_000m,
                UseMinimumMarketOrderSize = true
            });

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m, minOrderSize: 5m)));

        Assert.True(decision.Accepted);
        Assert.Equal(5m, decision.ProposedSizeShares);
        Assert.Equal(3.70m, decision.ProposedNotionalUsd);
    }

    [Fact]
    public void SignalEngine_RejectsSellBelowMarketMinimumWhenMinimumSizeConfigured()
    {
        var engine = CreateSignalEngine(
            paperOptions: new PaperTradingOptions
            {
                InitialBankrollUsd = 10_000m,
                UseMinimumMarketOrderSize = true
            });

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m, minOrderSize: 5m),
            trade: Trade() with { Side = TradeSide.Sell },
            availablePositionSizeShares: 4m));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.PaperPositionBelowMarketMinimum, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsSellWhenCopiedPositionIsMissing()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            trade: Trade() with { Side = TradeSide.Sell }));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.NoPaperPositionToSell, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsUnsupportedSide()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(
            OrderBook(0.73m, 0.75m),
            trade: Trade() with { Side = TradeSide.Unknown }));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.UnsupportedSide, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsWideAbsoluteSpread()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.72m, 0.76m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.SpreadTooWideAbs, decision.DecisionCode);
        Assert.Contains(SignalReasonCodes.SpreadTooWideAbs, decision.Reasons);
    }

    [Fact]
    public void SignalEngine_RejectsWidePercentageSpread()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.300m, 0.315m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.SpreadTooWidePct, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_UsesLeaderPriceEvenWhenBookMovedPastLeader()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.79m, 0.80m)));

        Assert.True(decision.Accepted);
        Assert.Equal(0.74m, decision.ProposedPrice);
    }

    [Fact]
    public void RiskEngine_RejectsTradeLimit()
    {
        var riskEngine = new DefaultRiskEngine(
            new RiskOptions { MaxTradeBankrollPct = 0.01m },
            new PaperTradingOptions { InitialBankrollUsd = 10_000m });

        var decision = riskEngine.Evaluate(
            new ProposedOrderIntent(
                Wallet,
                "condition-1",
                "asset-1",
                "POLITICS",
                TradeSide.Buy,
                0.50m,
                50m,
                25m),
            new ExposureSnapshot(0m, 0m, 0m, 0m, 0m, 0));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskTradeLimit, decision.ReasonCodes);
    }

    [Fact]
    public void SignalEngine_RejectsScoreBelowIgnoreThreshold()
    {
        var engine = CreateSignalEngine(new SignalOptions
        {
            IgnoreBelowScore = 96,
            ObserveBelowScore = 97,
            NormalPaperOrderScore = 98
        });

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.ScoreBelowThreshold, decision.DecisionCode);
    }

    [Fact]
    public void SignalEngine_RejectsObserveOnlyScore()
    {
        var engine = CreateSignalEngine(new SignalOptions
        {
            IgnoreBelowScore = 60,
            ObserveBelowScore = 96,
            NormalPaperOrderScore = 97
        });

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.ObserveOnly, decision.DecisionCode);
    }

    [Fact]
    public void RiskEngine_RejectsMarketExposureLimit()
    {
        var decision = CreateRiskEngine().Evaluate(
            Intent(25m),
            Exposure(marketExposureUsd: 90m));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskMarketLimit, decision.ReasonCodes);
    }

    [Fact]
    public void RiskEngine_RejectsTraderExposureLimit()
    {
        var decision = CreateRiskEngine().Evaluate(
            Intent(25m),
            Exposure(traderExposureUsd: 290m));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskTraderLimit, decision.ReasonCodes);
    }

    [Fact]
    public void RiskEngine_RejectsCategoryExposureLimit()
    {
        var decision = CreateRiskEngine().Evaluate(
            Intent(25m),
            Exposure(categoryExposureUsd: 740m));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskCategoryLimit, decision.ReasonCodes);
    }

    [Fact]
    public void RiskEngine_RejectsTotalDeployedLimit()
    {
        var decision = CreateRiskEngine().Evaluate(
            Intent(25m),
            Exposure(totalDeployedUsd: 2_490m));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskTotalDeployedLimit, decision.ReasonCodes);
    }

    [Fact]
    public void RiskEngine_RejectsDailyLossLimit()
    {
        var decision = CreateRiskEngine().Evaluate(
            Intent(25m),
            Exposure(dailyLossUsd: 101m));

        Assert.False(decision.Allowed);
        Assert.Contains(SignalReasonCodes.RiskDailyLossLimit, decision.ReasonCodes);
    }

    [Fact]
    public void SignalEngine_PersistsRiskReasonInDecision()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(
            CreateContext(
                OrderBook(0.73m, 0.75m),
                new ExposureSnapshot(
                    MarketExposureUsd: 90m,
                    TraderExposureUsd: 0m,
                    CategoryExposureUsd: 0m,
                    TotalDeployedUsd: 90m,
                    DailyLossUsd: 0m,
                    OpenOrdersCount: 0)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.RiskMarketLimit, decision.DecisionCode);
        Assert.Contains(SignalReasonCodes.RiskMarketLimit, decision.Reasons);
    }

    private static ISignalEngine CreateSignalEngine(
        SignalOptions? signalOptions = null,
        PaperTradingOptions? paperOptions = null)
    {
        var riskOptions = new RiskOptions
        {
            MaxTradeBankrollPct = 0.25m,
            MaxMarketBankrollPct = 1.0m,
            MaxTraderBankrollPct = 3.0m,
            MaxCategoryBankrollPct = 7.5m,
            MaxTotalDeployedPct = 25.0m,
            MaxDailyLossPct = 1.0m
        };
        paperOptions ??= new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new DefaultSignalEngine(
            signalOptions ?? new SignalOptions(),
            new ExecutionOptions(),
            riskOptions,
            paperOptions,
            new DefaultRiskEngine(riskOptions, paperOptions));
    }

    private static SignalEvaluationContext CreateContext(
        OrderBookSnapshot orderBook,
        ExposureSnapshot? exposure = null,
        LeaderTrade? trade = null,
        TraderRule? traderRule = null,
        MarketInfo? marketInfo = null,
        PolymarketOnChainWalletCategoryPerformance? leaderCategoryPerformance = null,
        PaperCopiedTraderPerformance? copiedTraderOverallPerformance = null,
        PaperCopiedTraderPerformance? copiedTraderCategoryPerformance = null,
        decimal? availablePositionSizeShares = null)
    {
        return new SignalEvaluationContext(
            trade ?? Trade(),
            traderRule ?? new TraderRule(
                Wallet,
                ["POLITICS"],
                MaxLagSeconds: 300,
                MaxSlippageCents: 1m,
                MaxSpreadCents: 2m,
                MaxSpreadPct: 3.0m,
                MinLeaderTradeUsd: 500m),
            marketInfo ?? new MarketInfo(
                "condition-1",
                "sample-market",
                "Will sample event happen?",
                "POLITICS",
                DateTimeOffset.UtcNow.AddDays(2)),
            orderBook,
            exposure ?? new ExposureSnapshot(0m, 0m, 0m, 0m, 0m, 0),
            leaderCategoryPerformance,
            copiedTraderOverallPerformance,
            copiedTraderCategoryPerformance,
            availablePositionSizeShares);
    }

    private static SignalOptions StrictCategorySignalOptions()
    {
        return new SignalOptions
        {
            RequireKnownMarketCategory = true,
            RequireLeaderCategoryPerformance = true,
            MinLeaderCategoryResolvedPositions = 3,
            MinLeaderCategoryResolvedRoiPct = 0m,
            MinLeaderCategoryWinRatePct = 50m,
            MinLeaderCategoryScore = 0m,
            MinLeaderCategorySampleQuality = "Low",
            LeaderCategoryPerformanceScore = 15
        };
    }

    private static PolymarketOnChainWalletCategoryPerformance GoodCategoryPerformance()
    {
        return new PolymarketOnChainWalletCategoryPerformance(
            Wallet,
            "POLITICS",
            PositionsCount: 12,
            OpenPositions: 2,
            FlatPositions: 3,
            ResolvedPositions: 7,
            ProfitableResolvedPositions: 5,
            LosingResolvedPositions: 2,
            MarketsTraded: 10,
            VolumeUsd: 5_000m,
            ResolvedVolumeUsd: 3_000m,
            OpenExposureUsd: 500m,
            ResolvedCostUsd: 2_000m,
            ResolvedPnlUsd: 250m,
            ResolvedRoiPct: 12.5m,
            WinRatePct: 71.4m,
            AveragePositionSizeUsd: 416.67m,
            Score: 120m,
            SampleQuality: "Low",
            FirstActiveUtc: DateTimeOffset.UtcNow.AddDays(-30),
            LastActiveUtc: DateTimeOffset.UtcNow.AddHours(-1),
            RefreshedAtUtc: DateTimeOffset.UtcNow);
    }

    private static PaperCopiedTraderPerformance CopiedPerformance(
        string category,
        int settledPositionsCount,
        decimal totalPnlUsd,
        decimal roiPct,
        decimal score)
    {
        var won = totalPnlUsd >= 0m ? settledPositionsCount : 0;
        var lost = settledPositionsCount - won;

        return new PaperCopiedTraderPerformance(
            Wallet,
            category,
            OrdersCount: settledPositionsCount,
            FilledOrdersCount: settledPositionsCount,
            BuyFillsCount: settledPositionsCount,
            SellFillsCount: 0,
            OpenPositionsCount: 0,
            SettledPositionsCount: settledPositionsCount,
            WonPositionsCount: won,
            LostPositionsCount: lost,
            BuyCostUsd: 100m,
            SellProceedsUsd: 0m,
            SettlementValueUsd: 100m + totalPnlUsd,
            RealizedPnlUsd: totalPnlUsd,
            UnrealizedPnlUsd: 0m,
            TotalPnlUsd: totalPnlUsd,
            RoiPct: roiPct,
            WinRatePct: settledPositionsCount == 0 ? 0m : won * 100m / settledPositionsCount,
            Score: score,
            FirstOrderUtc: DateTimeOffset.UtcNow.AddDays(-7),
            LastOrderUtc: DateTimeOffset.UtcNow.AddHours(-1),
            RefreshedAtUtc: DateTimeOffset.UtcNow);
    }

    private static DefaultRiskEngine CreateRiskEngine()
    {
        return new DefaultRiskEngine(
            new RiskOptions(),
            new PaperTradingOptions { InitialBankrollUsd = 10_000m });
    }

    private static ProposedOrderIntent Intent(decimal notionalUsd)
    {
        return new ProposedOrderIntent(
            Wallet,
            "condition-1",
            "asset-1",
            "POLITICS",
            TradeSide.Buy,
            0.50m,
            notionalUsd / 0.50m,
            notionalUsd);
    }

    private static ExposureSnapshot Exposure(
        decimal marketExposureUsd = 0m,
        decimal traderExposureUsd = 0m,
        decimal categoryExposureUsd = 0m,
        decimal totalDeployedUsd = 0m,
        decimal dailyLossUsd = 0m)
    {
        return new ExposureSnapshot(
            marketExposureUsd,
            traderExposureUsd,
            categoryExposureUsd,
            totalDeployedUsd,
            dailyLossUsd,
            0);
    }

    private static LeaderTrade Trade()
    {
        return new LeaderTrade(
            Wallet,
            "Gopfan",
            "condition-1",
            "asset-1",
            "sample-market",
            "Will sample event happen?",
            "Yes",
            TradeSide.Buy,
            0.74m,
            2_000m,
            1_480m,
            DateTimeOffset.UtcNow,
            "0xabc");
    }

    private static OrderBookSnapshot OrderBook(decimal bestBid, decimal bestAsk, decimal? minOrderSize = null)
    {
        return new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(bestBid, 1_000m)],
            [new OrderBookLevel(bestAsk, 1_000m)],
            DateTimeOffset.UtcNow,
            "condition-1",
            MinOrderSize: minOrderSize,
            TickSize: 0.01m);
    }
}

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
    public void SignalEngine_AcceptsBuySignalWithSafeMakerPrice()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.73m, 0.75m)));

        Assert.True(decision.Accepted);
        Assert.Equal("paper_order_small", decision.DecisionCode);
        Assert.Equal(0.74m, decision.ProposedPrice);
        Assert.True(decision.Score >= 75);
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
    public void SignalEngine_RejectsWhenPriceMovedTooFar()
    {
        var engine = CreateSignalEngine();

        var decision = engine.Evaluate(CreateContext(OrderBook(0.79m, 0.80m)));

        Assert.False(decision.Accepted);
        Assert.Equal(SignalReasonCodes.PriceMovedTooFar, decision.DecisionCode);
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

    private static ISignalEngine CreateSignalEngine()
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
        var paperOptions = new PaperTradingOptions { InitialBankrollUsd = 10_000m };
        return new DefaultSignalEngine(
            new SignalOptions(),
            new ExecutionOptions(),
            riskOptions,
            paperOptions,
            new DefaultRiskEngine(riskOptions, paperOptions));
    }

    private static SignalEvaluationContext CreateContext(
        OrderBookSnapshot orderBook,
        ExposureSnapshot? exposure = null)
    {
        return new SignalEvaluationContext(
            Trade(),
            new TraderRule(
                Wallet,
                ["POLITICS"],
                MaxLagSeconds: 300,
                MaxSlippageCents: 1m,
                MaxSpreadCents: 2m,
                MaxSpreadPct: 3.0m,
                MinLeaderTradeUsd: 500m),
            new MarketInfo(
                "condition-1",
                "sample-market",
                "Will sample event happen?",
                "POLITICS",
                DateTimeOffset.UtcNow.AddDays(2)),
            orderBook,
            exposure ?? new ExposureSnapshot(0m, 0m, 0m, 0m, 0m, 0));
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

    private static OrderBookSnapshot OrderBook(decimal bestBid, decimal bestAsk)
    {
        return new OrderBookSnapshot(
            "asset-1",
            [new OrderBookLevel(bestBid, 1_000m)],
            [new OrderBookLevel(bestAsk, 1_000m)],
            DateTimeOffset.UtcNow,
            "condition-1",
            TickSize: 0.01m);
    }
}

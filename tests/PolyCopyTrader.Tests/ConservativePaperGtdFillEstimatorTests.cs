using System.Text.Json;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Domain.Configuration;
using PolyCopyTrader.Service.PaperTrading;

namespace PolyCopyTrader.Tests;

public sealed class ConservativePaperGtdFillEstimatorTests
{
    [Fact]
    public void Evaluate_NonBtcOrder_IsNotHandled()
    {
        var now = DateTimeOffset.UtcNow;
        var estimator = CreateEstimator();
        var order = CreateOrder(
            now,
            StrategyIds.FollowLeader,
            RawDecisionJson(now));

        var result = estimator.Evaluate(order, null, now, previouslyFilledShares: 0m);

        Assert.False(result.Handled);
        Assert.Null(result.Fill);
    }

    [Fact]
    public void Evaluate_InitialMarketableAsk_CreatesImmediatePartialFill()
    {
        var now = DateTimeOffset.UtcNow;
        var estimator = CreateEstimator();
        var order = CreateOrder(
            now,
            StrategyIds.BtcUpDown5mVariants[0].Id,
            RawDecisionJson(
                now,
                initialExecutableAskShares: 6m,
                initialExecutableAskVwap: 0.48m));

        var result = estimator.Evaluate(order, null, now, previouslyFilledShares: 0m);

        Assert.True(result.Handled);
        Assert.NotNull(result.Fill);
        Assert.Equal(6m, result.Fill!.SizeShares);
        Assert.Equal(order.Price, result.Fill.Price);
        Assert.Contains("ConservativeGtdImmediateFill", result.Fill.Evidence);
        Assert.Contains("ObservedAskVwap=0.48", result.Fill.Evidence);
        Assert.Contains("filled_immediate_marketable", result.Order.RawDecisionJson);
    }

    [Fact]
    public void Evaluate_LaterAskBelowLimitWithoutTradeThrough_DoesNotFill()
    {
        var now = DateTimeOffset.UtcNow;
        var estimator = CreateEstimator();
        var order = CreateOrder(
            now.AddSeconds(-10),
            StrategyIds.BtcUpDown5mVariants[0].Id,
            RawDecisionJson(now.AddSeconds(-10)));
        var orderBook = new OrderBookSnapshot(
            "asset",
            [new OrderBookLevel(0.50m, 10m)],
            [new OrderBookLevel(0.49m, 10m)],
            now,
            LastTradePrice: 0.60m);

        var result = estimator.Evaluate(order, orderBook, now, previouslyFilledShares: 0m);

        Assert.True(result.Handled);
        Assert.Null(result.Fill);
        Assert.Contains("waiting_for_high_confidence_evidence", result.Order.RawDecisionJson);
    }

    [Fact]
    public void Evaluate_BookTradesThroughLimit_CreatesLateFillAtLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var estimator = CreateEstimator();
        var order = CreateOrder(
            now.AddSeconds(-10),
            StrategyIds.BtcUpDown5mVariants[0].Id,
            RawDecisionJson(now.AddSeconds(-10), initialQueueAheadShares: 12m));
        var orderBook = new OrderBookSnapshot(
            "asset",
            [new OrderBookLevel(0.48m, 10m)],
            [new OrderBookLevel(0.49m, 10m)],
            now,
            LastTradePrice: 0.49m);

        var result = estimator.Evaluate(order, orderBook, now, previouslyFilledShares: 0m);

        Assert.True(result.Handled);
        Assert.NotNull(result.Fill);
        Assert.Equal(order.SizeShares, result.Fill!.SizeShares);
        Assert.Equal(order.Price, result.Fill.Price);
        Assert.Contains("ConservativeGtdLateFill", result.Fill.Evidence);
        Assert.Contains("filled_late_trade_through_limit", result.Order.RawDecisionJson);
    }

    [Fact]
    public void Evaluate_LegacyGtdOrder_CapturesBaselineWithoutImmediateFill()
    {
        var now = DateTimeOffset.UtcNow;
        var estimator = CreateEstimator();
        var order = CreateOrder(
            now,
            StrategyIds.BtcUpDown5mVariants[0].Id,
            """
            {"pricing_mode":"opening_limit","order_type":"GTD","order_execution_mode":"GTD"}
            """);
        var orderBook = new OrderBookSnapshot(
            "asset",
            [new OrderBookLevel(0.50m, 10m)],
            [new OrderBookLevel(0.49m, 10m)],
            now,
            LastTradePrice: 0.49m);

        var result = estimator.Evaluate(order, orderBook, now, previouslyFilledShares: 0m);

        Assert.True(result.Handled);
        Assert.True(result.OrderChanged);
        Assert.Null(result.Fill);
        Assert.Contains("baseline_captured", result.Order.RawDecisionJson);
    }

    private static ConservativePaperGtdFillEstimator CreateEstimator()
    {
        return new ConservativePaperGtdFillEstimator(new BtcUpDown5mStrategyOptions());
    }

    private static PaperOrder CreateOrder(DateTimeOffset now, Guid strategyId, string rawDecisionJson)
    {
        return new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "btc-strategy",
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset",
            "condition",
            "Up",
            0.50m,
            10m,
            5m,
            now,
            now.AddMinutes(2),
            StrategyId: strategyId,
            RawDecisionJson: rawDecisionJson);
    }

    private static string RawDecisionJson(
        DateTimeOffset snapshotAtUtc,
        decimal initialQueueAheadShares = 5m,
        decimal initialExecutableAskShares = 0m,
        decimal? initialExecutableAskVwap = null)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["pricing_mode"] = "opening_limit",
            ["order_type"] = "GTD",
            ["order_execution_mode"] = "GTD",
            ["paper_gtd_initial_snapshot_at_utc"] = snapshotAtUtc.ToString("O"),
            ["paper_gtd_initial_best_bid"] = 0.50m,
            ["paper_gtd_initial_best_ask"] = 0.51m,
            ["paper_gtd_initial_last_trade_price"] = 0.50m,
            ["paper_gtd_initial_queue_ahead_shares"] = initialQueueAheadShares,
            ["paper_gtd_initial_executable_ask_shares"] = initialExecutableAskShares,
            ["paper_gtd_initial_executable_ask_vwap"] = initialExecutableAskVwap
        });
    }
}

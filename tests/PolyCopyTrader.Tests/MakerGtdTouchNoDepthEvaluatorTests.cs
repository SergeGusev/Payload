using PolyCopyTrader.Domain;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class MakerGtdTouchNoDepthEvaluatorTests
{
    private static readonly DateTimeOffset AcceptedAtUtc =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset EffectiveExpiresAtUtc =
        AcceptedAtUtc.AddMinutes(4);

    [Theory]
    [InlineData("0.49")]
    [InlineData("0.50")]
    public void Evaluate_AuthoritativeLastTradeAtOrBelowLimit_FillsAllSharesAtLimit(
        string priceText)
    {
        var evidence = LastTradeEvidence(
            decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture),
            AcceptedAtUtc.AddSeconds(1));

        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(Order(), evidence);

        AssertFullFill(
            result,
            MakerGtdTouchNoDepthTrigger.LastTradePrice,
            MakerGtdTouchNoDepthReasonCodes.LastTradeTouchedLimit,
            evidence.LastTradePrice!.Value,
            evidence.TimestampUtc);
    }

    [Fact]
    public void Evaluate_LastTradeAboveLimit_DoesNotFill()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.51m, AcceptedAtUtc.AddSeconds(1)));

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.LastTradeDidNotTouchLimit);
    }

    [Theory]
    [InlineData(MarketDataEventType.Book, "0.49")]
    [InlineData(MarketDataEventType.PriceChange, "0.50")]
    [InlineData(MarketDataEventType.BestBidAsk, "0.50")]
    public void Evaluate_CurrentBestAskAtOrBelowLimit_FillsAllSharesAtLimit(
        MarketDataEventType eventType,
        string priceText)
    {
        var evidence = BestAskEvidence(
            eventType,
            decimal.Parse(priceText, System.Globalization.CultureInfo.InvariantCulture),
            AcceptedAtUtc.AddSeconds(1));

        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(Order(), evidence);

        AssertFullFill(
            result,
            MakerGtdTouchNoDepthTrigger.BestAsk,
            MakerGtdTouchNoDepthReasonCodes.BestAskTouchedLimit,
            evidence.BestAsk!.Value,
            evidence.TimestampUtc);
    }

    [Fact]
    public void Evaluate_BestAskAboveLimit_DoesNotFill()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            BestAskEvidence(
                MarketDataEventType.BestBidAsk,
                0.51m,
                AcceptedAtUtc.AddSeconds(1)));

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.BestAskDidNotTouchLimit);
    }

    [Fact]
    public void Evaluate_StaleCrossingEvidence_DoesNotFill()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            BestAskEvidence(
                MarketDataEventType.BestBidAsk,
                0.49m,
                AcceptedAtUtc.AddSeconds(1)) with
            {
                IsCurrent = false
            });

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.StaleEvidence);
    }

    [Fact]
    public void Evaluate_DuplicateCrossingEvidence_DoesNotFill()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, AcceptedAtUtc.AddSeconds(1)) with
            {
                IsDuplicateEvent = true
            });

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.DuplicateEvent);
    }

    [Fact]
    public void Evaluate_NonAuthoritativeCrossingEvidence_DoesNotFill()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, AcceptedAtUtc.AddSeconds(1)) with
            {
                TimestampIsAuthoritative = false
            });

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.TimestampNotAuthoritative);
    }

    [Theory]
    [InlineData("asset-2", "condition-1", "maker_gtd_touch_no_depth_asset_mismatch")]
    [InlineData("asset-1", "condition-2", "maker_gtd_touch_no_depth_condition_mismatch")]
    public void Evaluate_NonExactMarketEvidence_DoesNotFill(
        string assetId,
        string conditionId,
        string expectedReasonCode)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, AcceptedAtUtc.AddSeconds(1)) with
            {
                AssetId = assetId,
                ConditionId = conditionId
            });

        AssertNoFill(result, expectedReasonCode);
    }

    [Theory]
    [MemberData(nameof(NotAfterAcceptanceTimestamps))]
    public void Evaluate_EventNotStrictlyAfterAcceptance_DoesNotFill(
        DateTimeOffset timestampUtc)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, timestampUtc));

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.NotAfterAcceptance);
    }

    [Theory]
    [MemberData(nameof(AtOrAfterExpiryTimestamps))]
    public void Evaluate_EventAtOrAfterEffectiveExpiry_DoesNotFill(
        DateTimeOffset timestampUtc)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, timestampUtc));

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.AtOrAfterEffectiveExpiry);
    }

    [Theory]
    [InlineData(MarketDataEventType.Unknown)]
    [InlineData(MarketDataEventType.TickSizeChange)]
    [InlineData(MarketDataEventType.MarketResolved)]
    public void Evaluate_UnsupportedEvent_DoesNotFill(MarketDataEventType eventType)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(0.49m, AcceptedAtUtc.AddSeconds(1)) with
            {
                EventType = eventType
            });

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.UnsupportedEvent);
    }

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public void Evaluate_MissingOrInvalidLastTradePrice_DoesNotFill(decimal? price)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            LastTradeEvidence(price, AcceptedAtUtc.AddSeconds(1)));

        AssertNoFill(
            result,
            price is null
                ? MakerGtdTouchNoDepthReasonCodes.MissingLastTradePrice
                : MakerGtdTouchNoDepthReasonCodes.InvalidLastTradePrice);
    }

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public void Evaluate_MissingOrInvalidBestAsk_DoesNotFill(decimal? price)
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            BestAskEvidence(
                MarketDataEventType.BestBidAsk,
                price,
                AcceptedAtUtc.AddSeconds(1)));

        AssertNoFill(
            result,
            price is null
                ? MakerGtdTouchNoDepthReasonCodes.MissingBestAsk
                : MakerGtdTouchNoDepthReasonCodes.InvalidBestAsk);
    }

    [Fact]
    public void Evaluate_CrossImmediatelyBeforeEffectiveExpiry_Fills()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order(),
            BestAskEvidence(
                MarketDataEventType.BestBidAsk,
                0.50m,
                EffectiveExpiresAtUtc.AddTicks(-1)));

        Assert.True(result.Filled);
    }

    [Fact]
    public void Evaluate_InvalidRestingOrder_FailsClosed()
    {
        var result = MakerGtdTouchNoDepthEvaluator.Evaluate(
            Order() with { RemainingShares = 0m },
            LastTradeEvidence(0.49m, AcceptedAtUtc.AddSeconds(1)));

        AssertNoFill(result, MakerGtdTouchNoDepthReasonCodes.InvalidOrder);
    }

    public static TheoryData<DateTimeOffset> NotAfterAcceptanceTimestamps => new()
    {
        AcceptedAtUtc.AddTicks(-1),
        AcceptedAtUtc
    };

    public static TheoryData<DateTimeOffset> AtOrAfterExpiryTimestamps => new()
    {
        EffectiveExpiresAtUtc,
        EffectiveExpiresAtUtc.AddTicks(1)
    };

    public static TheoryData<decimal?> InvalidPrices => new()
    {
        null,
        0m,
        -0.01m,
        1m,
        1.01m
    };

    private static MakerGtdRestingBuyOrder Order()
    {
        return new MakerGtdRestingBuyOrder(
            AssetId: "asset-1",
            ConditionId: "condition-1",
            LimitPrice: 0.50m,
            RemainingShares: 12.50m,
            AcceptedAtUtc,
            EffectiveExpiresAtUtc);
    }

    private static MakerGtdTouchNoDepthEvidence LastTradeEvidence(
        decimal? price,
        DateTimeOffset timestampUtc)
    {
        return new MakerGtdTouchNoDepthEvidence(
            MarketDataEventType.LastTradePrice,
            AssetId: "asset-1",
            ConditionId: "condition-1",
            LastTradePrice: price,
            BestAsk: null,
            TimestampUtc: timestampUtc,
            TimestampIsAuthoritative: true,
            IsCurrent: true);
    }

    private static MakerGtdTouchNoDepthEvidence BestAskEvidence(
        MarketDataEventType eventType,
        decimal? bestAsk,
        DateTimeOffset timestampUtc)
    {
        return new MakerGtdTouchNoDepthEvidence(
            eventType,
            AssetId: "asset-1",
            ConditionId: "condition-1",
            LastTradePrice: null,
            BestAsk: bestAsk,
            TimestampUtc: timestampUtc,
            TimestampIsAuthoritative: true,
            IsCurrent: true);
    }

    private static void AssertFullFill(
        MakerGtdTouchNoDepthEvaluation result,
        MakerGtdTouchNoDepthTrigger trigger,
        string reasonCode,
        decimal triggerPrice,
        DateTimeOffset triggerTimestampUtc)
    {
        Assert.True(result.Filled);
        Assert.Equal(MakerGtdTouchNoDepthOutcome.FullFill, result.Outcome);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Equal(0.50m, result.FillPrice);
        Assert.Equal(12.50m, result.FillShares);
        Assert.Equal(trigger, result.Trigger);
        Assert.Equal(triggerPrice, result.TriggerPrice);
        Assert.Equal(triggerTimestampUtc, result.TriggerTimestampUtc);
    }

    private static void AssertNoFill(
        MakerGtdTouchNoDepthEvaluation result,
        string reasonCode)
    {
        Assert.False(result.Filled);
        Assert.Equal(MakerGtdTouchNoDepthOutcome.NoFill, result.Outcome);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Equal(0m, result.FillPrice);
        Assert.Equal(0m, result.FillShares);
        Assert.Equal(MakerGtdTouchNoDepthTrigger.None, result.Trigger);
        Assert.Null(result.TriggerPrice);
        Assert.Null(result.TriggerTimestampUtc);
    }
}

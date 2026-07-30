using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.Strategies;

namespace PolyCopyTrader.Tests;

public sealed class FakBuyExecutionParityTests
{
    [Fact]
    public void SharedIntentPreservesHardPriceCapAcrossPaperAndLiveAdapters()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var orderBook = new OrderBookSnapshot(
            "123",
            [new OrderBookLevel(0.47m, 100m)],
            [
                new OrderBookLevel(0.48m, 1m),
                new OrderBookLevel(0.52m, 100m)
            ],
            nowUtc,
            ConditionId: "condition-1",
            MinOrderSize: 1m,
            TickSize: 0.01m,
            NegativeRisk: true);
        var intent = FakBuyExecutionIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "condition-1",
            orderBook.AssetId,
            maximumOrderPrice: 0.50m,
            targetNotionalUsd: 5.0094m,
            targetSizeShares: 10m,
            orderBook,
            nowUtc);

        var paperEstimate = FakBuyExecutionParity.SimulatePaper(
            intent,
            orderBook,
            maximumSpreadAbsolute: null);
        var liveRequest = FakBuyExecutionParity.CreateLiveRequest(
            intent,
            "0x1111111111111111111111111111111111111111",
            "0x2222222222222222222222222222222222222222",
            ClobV2SignatureType.EOA);

        Assert.True(paperEstimate.Filled);
        Assert.Equal(0.50m, paperEstimate.MaxAllowedPrice);
        Assert.Equal(0.48m, paperEstimate.AverageFillPrice);
        Assert.Equal(1m, paperEstimate.SizeShares);
        Assert.Equal(0.48m, paperEstimate.NotionalUsd);
        Assert.Equal(1, paperEstimate.LevelsUsed);
        Assert.Equal(5.0094m, intent.RequestedNotionalUsd);
        Assert.Equal(5m, intent.TargetNotionalUsd);

        Assert.Equal(intent.AssetId, liveRequest.TokenId);
        Assert.Equal(intent.Side, liveRequest.Side);
        Assert.Equal(intent.MaximumOrderPrice, liveRequest.Price);
        Assert.Equal(intent.TargetNotionalUsd, liveRequest.MarketBuyAmountUsd);
        Assert.Equal(intent.TargetSizeShares, liveRequest.SizeShares);
        Assert.Equal(intent.TickSize, liveRequest.TickSize);
        Assert.Equal(intent.MinOrderSize, liveRequest.MinOrderSize);
        Assert.Equal(intent.NegativeRisk, liveRequest.NegativeRisk);
        Assert.Equal(ClobV2OrderType.FAK, liveRequest.OrderType);
        Assert.False(liveRequest.PostOnly);

        var signedShape = new ClobV2OrderBuilder(new OrderAmountCalculator()).Build(liveRequest);
        Assert.Equal("5000000", signedShape.MakerAmount);
    }

    [Fact]
    public void PaperAdapterRejectsDifferentOrderBookThanIntent()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var intentBook = OrderBook("asset-a", nowUtc);
        var intent = FakBuyExecutionIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "condition-1",
            intentBook.AssetId,
            maximumOrderPrice: 0.50m,
            targetNotionalUsd: 5m,
            targetSizeShares: 10m,
            intentBook,
            nowUtc);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FakBuyExecutionParity.SimulatePaper(
                intent,
                OrderBook("asset-b", nowUtc),
                maximumSpreadAbsolute: null));

        Assert.Contains("asset does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedAdaptersRejectHardPriceCapThatIsNotTickAligned()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var orderBook = new OrderBookSnapshot(
            "123",
            [new OrderBookLevel(0.49m, 100m)],
            [new OrderBookLevel(0.50m, 100m)],
            nowUtc,
            ConditionId: "condition-1",
            MinOrderSize: 1m,
            TickSize: 0.01m);
        var intent = FakBuyExecutionIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "condition-1",
            orderBook.AssetId,
            maximumOrderPrice: 0.505m,
            targetNotionalUsd: 5m,
            targetSizeShares: 10m,
            orderBook,
            nowUtc);

        var validation = FakBuyExecutionParity.Validate(intent);
        var paperEstimate = FakBuyExecutionParity.SimulatePaper(
            intent,
            orderBook,
            maximumSpreadAbsolute: null);
        var liveError = Assert.Throws<ArgumentException>(() =>
            FakBuyExecutionParity.CreateLiveRequest(
                intent,
                "0x1111111111111111111111111111111111111111",
                "0x2222222222222222222222222222222222222222",
                ClobV2SignatureType.EOA));

        Assert.Equal(0.505m, intent.MaximumOrderPrice);
        Assert.False(validation.IsValid);
        var rejectionReason = Assert.IsType<string>(validation.RejectionReason);
        Assert.Equal("Price must align to the configured tick size.", rejectionReason);
        Assert.False(paperEstimate.Filled);
        Assert.Equal(0m, paperEstimate.SizeShares);
        Assert.Equal(0m, paperEstimate.NotionalUsd);
        Assert.Equal(0.505m, paperEstimate.MaxAllowedPrice);
        Assert.Equal(rejectionReason, paperEstimate.RejectionReason);
        Assert.Contains(rejectionReason, liveError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedAdaptersRejectMarketBuyWhoseEffectiveWorstPriceSizeIsBelowMinimum()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
        var orderBook = new OrderBookSnapshot(
            "123",
            [new OrderBookLevel(0.09m, 100m)],
            [new OrderBookLevel(0.10m, 100m)],
            nowUtc,
            ConditionId: "condition-1",
            MinOrderSize: 5m,
            TickSize: 0.01m);
        var intent = FakBuyExecutionIntent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "condition-1",
            orderBook.AssetId,
            maximumOrderPrice: 0.50m,
            targetNotionalUsd: 1m,
            targetSizeShares: 10m,
            orderBook,
            nowUtc);

        var validation = FakBuyExecutionParity.Validate(intent);
        var paperEstimate = FakBuyExecutionParity.SimulatePaper(
            intent,
            orderBook,
            maximumSpreadAbsolute: null);
        var liveError = Assert.Throws<ArgumentException>(() =>
            FakBuyExecutionParity.CreateLiveRequest(
                intent,
                "0x1111111111111111111111111111111111111111",
                "0x2222222222222222222222222222222222222222",
                ClobV2SignatureType.EOA));

        Assert.Equal(2m, intent.TargetSizeShares);
        Assert.False(validation.IsValid);
        var rejectionReason = Assert.IsType<string>(validation.RejectionReason);
        Assert.Equal(
            "Market BUY amount must buy at least the configured minimum order size at the worst price.",
            rejectionReason);
        Assert.False(paperEstimate.Filled);
        Assert.Equal(0m, paperEstimate.SizeShares);
        Assert.Equal(0m, paperEstimate.NotionalUsd);
        Assert.Equal(intent.TargetSizeShares, paperEstimate.TargetSizeShares);
        Assert.Equal(rejectionReason, paperEstimate.RejectionReason);
        Assert.Contains(rejectionReason, liveError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPartialNotionalToleranceUsesOneMicrodollarBoundary()
    {
        const decimal targetNotionalUsd = 5m;

        Assert.False(FakExecutionRules.IsPartialNotionalFill(4.999999m, targetNotionalUsd));
        Assert.False(FakExecutionRules.IsPartialNotionalFill(5m, targetNotionalUsd));
        Assert.True(FakExecutionRules.IsPartialNotionalFill(4.9999989m, targetNotionalUsd));
        Assert.Equal(0.000001m, FakExecutionRules.PartialFillNotionalToleranceUsd);
    }

    private static OrderBookSnapshot OrderBook(string assetId, DateTimeOffset nowUtc)
    {
        return new OrderBookSnapshot(
            assetId,
            [new OrderBookLevel(0.47m, 100m)],
            [new OrderBookLevel(0.48m, 100m)],
            nowUtc,
            ConditionId: "condition-1",
            MinOrderSize: 1m,
            TickSize: 0.01m);
    }
}

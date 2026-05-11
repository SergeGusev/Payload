using PolyCopyTrader.Domain;
using PolyCopyTrader.Strategy;

namespace PolyCopyTrader.Tests;

public sealed class TakerBuyFillEstimatorTests
{
    [Fact]
    public void EstimateMinimumBuyNotional_UsesVwapForMarketMinimumShares()
    {
        var minimum = TakerBuyFillEstimator.EstimateMinimumBuyNotional(
            Book(
                bids: [new OrderBookLevel(0.48m, 100m)],
                asks:
                [
                    new OrderBookLevel(0.50m, 2m),
                    new OrderBookLevel(0.60m, 10m)
                ],
                minOrderSize: 5m),
            maxAllowedPrice: 0.60m,
            minOrderSize: 5m);

        Assert.True(minimum.Available);
        Assert.Equal(5m, minimum.MinOrderSize);
        Assert.Equal(2.80m, minimum.NotionalUsd);
        Assert.Equal(0.56m, minimum.AveragePrice);
        Assert.Equal(2, minimum.LevelsUsed);
    }

    [Fact]
    public void Estimate_UsesVwapAcrossAskDepth()
    {
        var orderBook = Book(
            bids: [new OrderBookLevel(0.50m, 100m)],
            asks:
            [
                new OrderBookLevel(0.51m, 5m),
                new OrderBookLevel(0.52m, 10m),
                new OrderBookLevel(0.53m, 20m)
            ]);

        var estimate = TakerBuyFillEstimator.Estimate(
            orderBook,
            targetNotionalUsd: 7.75m,
            maxAllowedPrice: 0.52m);

        Assert.True(estimate.Filled);
        Assert.Equal(15m, estimate.SizeShares);
        Assert.Equal(7.75m, estimate.NotionalUsd);
        Assert.Equal(0.5166666666666666666666666667m, estimate.AverageFillPrice);
        Assert.Equal(2, estimate.LevelsUsed);
    }

    [Fact]
    public void Estimate_FillsTargetNotionalWhenPriceIsBelowMaxAllowedPrice()
    {
        var estimate = TakerBuyFillEstimator.Estimate(
            Book(
                bids: [new OrderBookLevel(0.15m, 100m)],
                asks: [new OrderBookLevel(0.16m, 100m)]),
            targetNotionalUsd: 5.00m,
            maxAllowedPrice: 0.525m);

        Assert.True(estimate.Filled);
        Assert.Equal(31.25m, estimate.SizeShares);
        Assert.Equal(5.00m, estimate.NotionalUsd);
        Assert.Equal(0.16m, estimate.AverageFillPrice);
        Assert.Equal(1, estimate.LevelsUsed);
    }

    [Fact]
    public void Estimate_RejectsWhenBestAskExceedsMaxAllowedPrice()
    {
        var estimate = TakerBuyFillEstimator.Estimate(
            Book(
                bids: [new OrderBookLevel(0.50m, 100m)],
                asks: [new OrderBookLevel(0.53m, 100m)]),
            targetNotionalUsd: 7.80m,
            maxAllowedPrice: 0.52m);

        Assert.False(estimate.Filled);
        Assert.Equal(SignalReasonCodes.BestAskAboveMaxEntry, estimate.RejectionReason);
    }

    [Fact]
    public void Estimate_FakFillsAvailableDepthWithinMaxPriceWithoutRejectingPartial()
    {
        var estimate = TakerBuyFillEstimator.Estimate(
            Book(
                bids: [new OrderBookLevel(0.50m, 100m)],
                asks:
                [
                    new OrderBookLevel(0.51m, 5m),
                    new OrderBookLevel(0.53m, 100m)
                ]),
            targetNotionalUsd: 7.80m,
            maxAllowedPrice: 0.52m);

        Assert.True(estimate.Filled);
        Assert.Equal(5m, estimate.SizeShares);
        Assert.Equal(2.55m, estimate.NotionalUsd);
        Assert.Equal(0.51m, estimate.AverageFillPrice);
        Assert.Equal(15.294117647058823529411764706m, estimate.TargetSizeShares);
    }

    [Fact]
    public void Estimate_RejectsWhenTargetSizeIsBelowMarketMinimum()
    {
        var estimate = TakerBuyFillEstimator.Estimate(
            Book(
                bids: [new OrderBookLevel(0.48m, 100m)],
                asks: [new OrderBookLevel(0.50m, 100m)],
                minOrderSize: 5m),
            targetNotionalUsd: 1.00m,
            maxAllowedPrice: 0.50m);

        Assert.False(estimate.Filled);
        Assert.Equal(SignalReasonCodes.OrderBelowMinSize, estimate.RejectionReason);
    }

    private static OrderBookSnapshot Book(
        IReadOnlyList<OrderBookLevel> bids,
        IReadOnlyList<OrderBookLevel> asks,
        decimal? minOrderSize = null)
    {
        return new OrderBookSnapshot(
            "asset-1",
            bids,
            asks,
            DateTimeOffset.UtcNow,
            "condition-1",
            minOrderSize);
    }
}

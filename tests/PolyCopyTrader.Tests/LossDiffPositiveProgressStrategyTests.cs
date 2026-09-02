using Microsoft.Extensions.Logging.Abstractions;
using PolyCopyTrader.Domain;
using PolyCopyTrader.Polymarket;
using PolyCopyTrader.Polymarket.Auth;
using PolyCopyTrader.Service.PaperTrading;
using PolyCopyTrader.Service.Strategies;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class LossDiffPositiveProgressStrategyTests
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    internal static BtcUpDown5mStrategyVariant Child(int bps, int cap) => StrategyIds.UpDown5mStrategyVariants.Single(v =>
        v.Code == $"eth_up_down_5m_up_bps_{bps}_fak_premarket_lossdiff_positive_progress_cap_{cap}");

    [Fact]
    public void Catalog_Exactly34CapsAndParents_NotInImmutableBaseline()
    {
        var variants = StrategyIds.UpDown5mStrategyVariants.Where(v =>
            v.Behavior == BtcUpDown5mStrategyBehavior.LossDiffPositiveProgressMirror).ToArray();
        Assert.Equal(34, variants.Length);
        Assert.Equal(34, variants.Select(v => v.Id).Distinct().Count());
        foreach (var (bps, max, group) in new[] { (4, 16, 8236), (8, 18, 8237) })
        {
            for (var cap = 1; cap <= max; cap++)
            {
                var child = Child(bps, cap);
                Assert.Equal(Guid.Parse($"b7c50005-0000-4000-{group}-{cap:000000000000}"), child.Id);
                Assert.Equal(Guid.Parse($"b7c50005-0000-4000-8137-{100 + bps:000000000000}"), child.ParentStrategyId);
                Assert.Equal($"ETH 5m Up {bps} bps Reference Average Premarket LossDiff Positive Progress Cap {cap}", child.Name);
                Assert.Equal(cap, child.LossDiffProgressCap);
                Assert.Equal(1, child.DecisionDepth);
                Assert.False(child.PaperOnly);
                Assert.DoesNotContain(StrategyIds.LegacyBaselineUpDown5mStrategyVariants, v => v.Id == child.Id);
            }
        }
        Assert.Equal(4, StrategyIds.UpDown5mStrategyVariants.Count(v => v.Behavior is
            BtcUpDown5mStrategyBehavior.LossDiffResetMirror or BtcUpDown5mStrategyBehavior.LossDiffPositiveMirror));
    }

    [Theory]
    [InlineData(4, 1, 20, 1)]
    [InlineData(4, 5, 12, 5)]
    [InlineData(4, 5, 3, 3)]
    [InlineData(4, 16, 18, 16)]
    [InlineData(8, 18, 20, 18)]
    [InlineData(8, 18, 0, 0)]
    public void Intent_UsesActualParentNotionalAndCap(int bps, int cap, int current, int multiplier)
    {
        var child = Child(bps, cap);
        var book = Book(1000m);
        var parent = ParentIntent(child, book);
        var run = Run(child.ParentStrategyId!.Value, Now.AddSeconds(-1), null, null) with { StakeUsd = 2.507m };
        var intent = BtcUpDown5mPaperStrategyProcessor.CreateLossDiffPositiveProgressIntent(child, State(child, current), run, parent, book, Now);
        if (current == 0) { Assert.Null(intent); return; }
        Assert.NotNull(intent);
        Assert.Equal(2.507m * multiplier, intent.RequestedNotionalUsd);
        Assert.Equal(parent.MaximumOrderPrice, intent.MaximumOrderPrice);
        Assert.Equal(parent.AssetId, intent.AssetId);
        Assert.Equal(child.Id, intent.StrategyId);
        var live = FakBuyExecutionParity.CreateLiveRequest(intent, "0x1111111111111111111111111111111111111111",
            "0x2222222222222222222222222222222222222222", ClobV2SignatureType.EOA);
        Assert.Equal(intent.TargetNotionalUsd, live.MarketBuyAmountUsd);
        Assert.Equal(ClobV2OrderType.FAK, live.OrderType);
        Assert.False(live.PostOnly);
        Assert.Equal(20m, parent.RequestedNotionalUsd);
    }

    [Theory]
    [InlineData(1000, true, 10)]
    [InlineData(4, true, 1.6)]
    [InlineData(0, false, 0)]
    public void IncreasedIntent_DepthFullPartialNoFill_DoesNotChangeFrozenIntent(int depth, bool filled, decimal notional)
    {
        var child = Child(4, 5);
        var book = Book(depth);
        var intent = BtcUpDown5mPaperStrategyProcessor.CreateLossDiffPositiveProgressIntent(child, State(child, 12),
            Run(child.ParentStrategyId!.Value, Now.AddSeconds(-1), null, null), ParentIntent(child, book), book, Now)!;
        var before = intent with { };
        var estimate = FakBuyExecutionParity.SimulatePaper(intent, book, null);
        Assert.Equal(filled, estimate.Filled);
        Assert.Equal(notional, estimate.NotionalUsd);
        Assert.Equal(before, intent);
        Assert.Equal(10m, intent.TargetNotionalUsd);
        Assert.Equal(0.6m, intent.MaximumOrderPrice);
    }

    [Fact]
    public void Intent_RejectsNonEntryAndDifferentParent()
    {
        var child = Child(8, 3);
        var book = Book(10);
        var run = Run(child.ParentStrategyId!.Value, Now.AddSeconds(-1), null, null);
        foreach (var invalid in new[] { run with { Status = StrategyMarketPaperRunStatuses.Skipped }, run with { StakeUsd = 0m }, run with { StrategyId = Guid.NewGuid() } })
            Assert.Throws<InvalidOperationException>(() => BtcUpDown5mPaperStrategyProcessor.CreateLossDiffPositiveProgressIntent(
                child, State(child, 2), invalid, ParentIntent(child, book), book, Now));
    }

    [Fact]
    public async Task Counter_CausalSettlementOrderZeroFloorNeutralCutoffAndDuplicates()
    {
        var child = Child(4, 5);
        var repository = new TestAppRepository();
        var start = Now.AddHours(-1);
        repository.StrategyChildParentAssignments.Add(new StrategyChildParentAssignment(Guid.NewGuid(), child.Id,
            child.ParentStrategyId!.Value, "ETH", 0, "LossDiffPositive", 0, 0, start, null, start));
        var loss = Run(child.ParentStrategyId.Value, start.AddMinutes(2), start.AddMinutes(3), -1);
        repository.StrategyMarketPaperRuns.AddRange([
            Run(child.ParentStrategyId.Value, start.AddSeconds(-1), start.AddMinutes(1), -1),
            Run(child.ParentStrategyId.Value, start, start.AddMinutes(4), 1),
            loss, loss,
            Run(child.ParentStrategyId.Value, start.AddMinutes(5), start.AddMinutes(6), 0),
            Run(child.ParentStrategyId.Value, start.AddMinutes(7), Now, -1),
            Run(child.ParentStrategyId.Value, start.AddMinutes(8), Now.AddMinutes(1), -1),
            Run(child.Id, start.AddMinutes(9), start.AddMinutes(10), -1)]);
        var first = await ((IAppRepository)repository).ReconcileStrategyLossDiffStatesAsync(child.ParentStrategyId.Value, Now);
        Assert.Equal(0, first[child.Id].CurrentValue);
        var later = await ((IAppRepository)repository).ReconcileStrategyLossDiffStatesAsync(child.ParentStrategyId.Value, Now.AddTicks(10));
        Assert.Equal(1, later[child.Id].CurrentValue);
        var repeated = await ((IAppRepository)repository).ReconcileStrategyLossDiffStatesAsync(child.ParentStrategyId.Value, Now.AddTicks(10));
        Assert.Equal(later[child.Id].CurrentValue, repeated[child.Id].CurrentValue);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(7, false)]
    public async Task Net_FeePipelineUsesActualChildFillAndIndependentRounding(int depth, bool won)
    {
        var child = Child(4, 5);
        var book = Book(depth) with { Asks = [new OrderBookLevel(0.4137m, depth)] };
        var intent = BtcUpDown5mPaperStrategyProcessor.CreateLossDiffPositiveProgressIntent(child, State(child, 12),
            Run(child.ParentStrategyId!.Value, Now.AddSeconds(-1), null, null), ParentIntent(child, book), book, Now)!;
        var fill = FakBuyExecutionParity.SimulatePaper(intent, book, null);
        Assert.True(fill.Filled);
        var order = new PaperOrder(Guid.NewGuid(), Guid.NewGuid(), child.CopiedTraderWallet, PaperOrderStatus.Filled,
            TradeSide.Buy, "asset", "condition", "Down", fill.AverageFillPrice, fill.SizeShares, fill.NotionalUsd, Now, Now.AddMinutes(5), FilledAtUtc: Now);
        var paperFill = new PaperFill(Guid.NewGuid(), order.Id, fill.AverageFillPrice, fill.SizeShares, Now, "test", FeeLiquidityRole: "Taker");
        var gross = (won ? fill.SizeShares : 0m) - fill.NotionalUsd;
        var run = Run(child.Id, Now, Now.AddMinutes(5), gross) with { PaperOrderId = order.Id, StakeUsd = fill.NotionalUsd, SizeShares = fill.SizeShares };
        var service = new PolymarketFeeAccountingService(NullLogger<PolymarketFeeAccountingService>.Instance, new FeeClient());
        var result = await service.ApplyToEntryBatchAsync(new PaperEntryPersistenceBatch([], [order], [paperFill], [], [], [run]));
        var expectedFee = decimal.Round(fill.SizeShares * 0.07m * fill.AverageFillPrice * (1m - fill.AverageFillPrice), 5, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedFee, Assert.Single(result.PaperFills).FeeUsd);
        var accounted = Assert.Single(result.StrategyRuns);
        Assert.Equal("Calculated", accounted.FeeAccountingStatus);
        Assert.Equal(gross - expectedFee, accounted.NetRealizedPnlUsd);
        Assert.Equal(fill.NotionalUsd, accounted.StakeUsd);
    }

    internal static StrategyMarketPaperRun Run(Guid strategyId, DateTimeOffset entered, DateTimeOffset? settled, decimal? gross) =>
        new(Guid.NewGuid(), strategyId, "market", "condition", "slug", "test", "ETH", entered, entered.AddMinutes(5), entered, entered,
            settled.HasValue ? StrategyMarketPaperRunStatuses.Settled : StrategyMarketPaperRunStatuses.Entered,
            "asset", "Down", 0.4m, 2m, 5m, null, null, entered, null, null, gross, settled, null, entered, entered);

    private static StrategyLossDiffState State(BtcUpDown5mStrategyVariant child, int current) =>
        new(child.Id, child.ParentStrategyId!.Value, "LossDiffPositive", 1, current, Now.AddHours(-1), null, null, Now, Now);
    private static OrderBookSnapshot Book(decimal depth) => new("asset", [new OrderBookLevel(0.39m, 1000m)],
        [new OrderBookLevel(0.4m, depth)], Now, "condition", 1m, 0.01m);
    private static FakBuyExecutionIntent ParentIntent(BtcUpDown5mStrategyVariant child, OrderBookSnapshot book) =>
        FakBuyExecutionIntent.Create(child.ParentStrategyId!.Value, Guid.NewGuid(), "condition", "asset", 0.6m, 20m, 20m / 0.6m, book, Now);

    private sealed class FeeClient : IPolymarketClobPublicClient
    {
        public Task<PolymarketClobMarketInfo> GetClobMarketInfoAsync(string conditionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PolymarketClobMarketInfo(conditionId, 0, 0, new(0.07m, 1, true), "{}"));
        public Task<OrderBookSnapshot?> GetOrderBookAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset> GetServerTimeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal?> GetMidpointAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<decimal?> GetSpreadAsync(string assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PolymarketClobMarketByToken?> GetMarketByTokenAsync(string tokenId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

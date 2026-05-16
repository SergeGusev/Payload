using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class StrategyPerformanceTests
{
    [Fact]
    public async Task GetStrategyPerformanceAsync_ComputesClosedOutcomeQualityMetrics()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        AddSettlement(repository, now, "asset-win-1", won: true, realizedPnlUsd: 2m);
        AddSettlement(repository, now, "asset-win-2", won: true, realizedPnlUsd: 4m);
        AddSettlement(repository, now, "asset-loss-1", won: false, realizedPnlUsd: -9m);

        var row = (await repository.GetStrategyPerformanceAsync()).Single(item => item.StrategyId == StrategyIds.FollowLeader);

        Assert.Equal(2, row.WonPositionsCount);
        Assert.Equal(1, row.LostPositionsCount);
        Assert.Equal(3m, row.AvgWinPnlUsd);
        Assert.Equal(-9m, row.AvgLossPnlUsd);
        Assert.Equal(6m / 9m, row.ProfitFactor);
        Assert.Equal(-1m, row.ExpectancyPnlUsd);
    }

    [Fact]
    public async Task GetStrategyPerformanceAsync_ComputesClosedRoiFromSettledStakeOnly()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.Less, 30);
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            "market-1",
            "condition-1",
            "btc-updown-5m-sample",
            "BTC Up or Down 5m",
            "Crypto",
            now.AddMinutes(-5),
            now,
            now.AddMinutes(-5),
            now.AddMinutes(-5),
            StrategyMarketPaperRunStatuses.Settled,
            "asset-1",
            "Up",
            0.50m,
            2m,
            4m,
            null,
            null,
            now.AddMinutes(-4.5),
            1m,
            4m,
            2m,
            now,
            null,
            now.AddMinutes(-5),
            now));

        var row = (await repository.GetStrategyPerformanceAsync()).Single(item => item.StrategyId == variant.Id);

        Assert.Equal(100m, row.ClosedRoiPct);
        Assert.Equal(100m, row.RoiPct);
        Assert.Equal(30m, row.AvgEntryDelaySeconds);
        Assert.Equal(30m, row.MaxEntryDelaySeconds);
    }

    [Fact]
    public async Task GetStrategyPerformanceAsync_KeepsClosedRoiSeparateFromOpenUnrealizedPnl()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0xleader",
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-open",
            "condition-open",
            "Yes",
            0.50m,
            20m,
            10m,
            now.AddMinutes(-5),
            now.AddMinutes(5),
            now.AddMinutes(-5),
            StrategyId: StrategyIds.FollowLeader));
        repository.PaperPositions.Add(new PaperPosition(
            "asset-open",
            "condition-open",
            "Yes",
            20m,
            0.50m,
            12m,
            2m,
            now,
            "0xleader"));
        repository.PaperPositionSettlements.Add(new PaperPositionSettlement(
            Guid.NewGuid(),
            "0xleader",
            "asset-closed",
            "condition-closed",
            "Yes",
            "asset-closed",
            "Yes",
            "Politics",
            10m,
            0.50m,
            5m,
            6m,
            1m,
            Won: true,
            "test",
            now,
            now));

        var row = (await repository.GetStrategyPerformanceAsync()).Single(item => item.StrategyId == StrategyIds.FollowLeader);

        Assert.Equal(30m, row.RoiPct);
        Assert.Equal(20m, row.ClosedRoiPct);
    }

    [Fact]
    public async Task GetStrategyPerformanceAsync_ComputesLiveOutcomeMetricsSeparatelyFromPaper()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var strategyId = StrategyIds.FollowLeader;
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xwin",
            TradeSide.Buy,
            "asset-win",
            "condition-live",
            "Yes",
            0.40m,
            10m,
            4m,
            "GTC",
            now.AddMinutes(-10),
            now.AddMinutes(5),
            now.AddMinutes(-10),
            "matched",
            10m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-9),
            StrategyId: strategyId,
            AverageFillPrice: 0.40m,
            FilledNotionalUsd: 4m,
            CostBasisUsd: 4m,
            SettlementValueUsd: 10m,
            RealizedPnlUsd: 6m,
            SettledAtUtc: now,
            WinningOutcome: "Yes",
            Won: true,
            SettlementSource: "test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xloss",
            TradeSide.Buy,
            "asset-loss",
            "condition-live",
            "No",
            0.50m,
            8m,
            4m,
            "GTC",
            now.AddMinutes(-8),
            now.AddMinutes(5),
            now.AddMinutes(-8),
            "matched",
            8m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-7),
            StrategyId: strategyId,
            AverageFillPrice: 0.50m,
            FilledNotionalUsd: 4m,
            CostBasisUsd: 4m,
            SettlementValueUsd: 0m,
            RealizedPnlUsd: -4m,
            SettledAtUtc: now,
            WinningOutcome: "Yes",
            Won: false,
            SettlementSource: "test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Live,
            "0xopen",
            TradeSide.Buy,
            "asset-open-live",
            "condition-live",
            "Yes",
            0.30m,
            10m,
            3m,
            "GTC",
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now.AddMinutes(-1),
            "live",
            0m,
            10m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-1),
            StrategyId: strategyId));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.PreflightRejected,
            null,
            TradeSide.Buy,
            "asset-rejected-live",
            "condition-live",
            "Yes",
            0.30m,
            10m,
            3m,
            "GTC",
            now.AddMinutes(-2),
            now.AddMinutes(5),
            now.AddMinutes(-2),
            "preflight_rejected",
            0m,
            10m,
            "test rejection",
            "{}",
            string.Empty,
            now.AddMinutes(-2),
            StrategyId: strategyId));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Rejected,
            null,
            TradeSide.Buy,
            "asset-live-clob-rejected",
            "condition-live",
            "Yes",
            0.30m,
            10m,
            3m,
            "GTC",
            now.AddMinutes(-2),
            now.AddMinutes(5),
            now.AddMinutes(-2),
            "rejected",
            0m,
            10m,
            "test clob rejection",
            "{}",
            string.Empty,
            now.AddMinutes(-2),
            StrategyId: strategyId));

        var row = (await repository.GetStrategyPerformanceAsync()).Single(item => item.StrategyId == strategyId);

        Assert.Equal(5, row.LiveOrdersCount);
        Assert.Equal(2, row.LiveFilledOrdersCount);
        Assert.Equal(1, row.LiveOpenOrdersCount);
        Assert.Equal(2, row.LiveSettledOrdersCount);
        Assert.Equal(2, row.LiveSkippedOrdersCount);
        Assert.Equal(0, row.LiveConditionSkippedOrdersCount);
        Assert.Equal(1, row.LiveTechnicalSkippedOrdersCount);
        Assert.Equal(1, row.LiveRejectedOrdersCount);
        Assert.Equal(1, row.LiveWonOrdersCount);
        Assert.Equal(1, row.LiveLostOrdersCount);
        Assert.Equal(8m, row.LiveStakeUsd);
        Assert.Equal(2m, row.LiveRealizedPnlUsd);
        Assert.Equal(50m, row.LiveWinRatePct);
        Assert.Equal(50m, row.LiveLossRatePct);
        Assert.Equal(6m, row.LiveAvgWinPnlUsd);
        Assert.Equal(-4m, row.LiveAvgLossPnlUsd);
        Assert.Equal(1.5m, row.LiveProfitFactor);
        Assert.Equal(1m, row.LiveExpectancyPnlUsd);
        Assert.Equal(25m, row.LiveRoiPct);
    }

    [Fact]
    public async Task GetStrategyRecentPerformanceAsync_ComputesWindowMetrics()
    {
        var repository = new TestAppRepository();
        var now = DateTimeOffset.UtcNow;
        var variant = StrategyIds.GetBtcUpDown5mVariant(BtcUpDown5mStrategyDirection.More, 90);
        repository.StrategySettings[variant.Id] = StrategyRuntimeSettings.Default(variant.Id) with { LiveStakes = true };
        var filledOrder = new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:" + variant.Code,
            PaperOrderStatus.Filled,
            TradeSide.Buy,
            "asset-filled",
            "condition-recent",
            "Up",
            0.50m,
            6m,
            3m,
            now.AddMinutes(-30),
            now.AddMinutes(90),
            now.AddMinutes(-29),
            StrategyId: variant.Id);
        repository.PaperOrders.Add(filledOrder);
        repository.PaperFills.Add(new PaperFill(
            Guid.NewGuid(),
            filledOrder.Id,
            0.50m,
            6m,
            now.AddMinutes(-29),
            "test"));
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:" + variant.Code,
            PaperOrderStatus.Expired,
            TradeSide.Buy,
            "asset-expired",
            "condition-recent",
            "Down",
            0.45m,
            8m,
            3.6m,
            now.AddMinutes(-20),
            now.AddMinutes(-18),
            StrategyId: variant.Id));
        repository.PaperOrders.Add(new PaperOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "strategy:" + variant.Code,
            PaperOrderStatus.Pending,
            TradeSide.Buy,
            "asset-open",
            "condition-recent",
            "Up",
            0.40m,
            10m,
            4m,
            now.AddMinutes(-10),
            now.AddMinutes(5),
            StrategyId: variant.Id));
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            "market-settled",
            "condition-recent",
            "btc-updown-5m-recent",
            "BTC Up or Down 5m",
            "Crypto",
            now.AddMinutes(-35),
            now.AddMinutes(-5),
            now.AddMinutes(-35),
            now.AddMinutes(-31),
            StrategyMarketPaperRunStatuses.Settled,
            "asset-filled",
            "Up",
            0.50m,
            3m,
            6m,
            filledOrder.SignalId,
            filledOrder.Id,
            now.AddMinutes(-30),
            1m,
            6m,
            3m,
            now.AddMinutes(-5),
            null,
            now.AddMinutes(-35),
            now.AddMinutes(-5)));
        repository.StrategyMarketPaperRuns.Add(new StrategyMarketPaperRun(
            Guid.NewGuid(),
            variant.Id,
            "market-skipped",
            "condition-skip",
            "btc-updown-5m-skip",
            "BTC Up or Down 5m",
            "Crypto",
            now.AddMinutes(-3),
            now.AddMinutes(2),
            now.AddMinutes(-3),
            now.AddMinutes(-2),
            StrategyMarketPaperRunStatuses.Skipped,
            null,
            null,
            null,
            0m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "btc_reference_move_below_bps_threshold",
            now.AddMinutes(-3),
            now.AddMinutes(-2)));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched,
            "0xlivewin",
            TradeSide.Buy,
            "asset-live-win",
            "condition-live-recent",
            "Up",
            0.40m,
            10m,
            4m,
            "GTC",
            now.AddMinutes(-25),
            now.AddMinutes(5),
            now.AddMinutes(-25),
            "matched",
            10m,
            0m,
            string.Empty,
            "{}",
            string.Empty,
            now.AddMinutes(-24),
            StrategyId: variant.Id,
            AverageFillPrice: 0.40m,
            FilledNotionalUsd: 4m,
            CostBasisUsd: 4m,
            SettlementValueUsd: 10m,
            RealizedPnlUsd: 6m,
            SettledAtUtc: now.AddMinutes(-4),
            WinningOutcome: "Up",
            Won: true,
            SettlementSource: "test"));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.PreflightRejected,
            null,
            TradeSide.Buy,
            "asset-live-rejected",
            "condition-live-recent",
            "Down",
            0.50m,
            8m,
            4m,
            "GTC",
            now.AddMinutes(-15),
            now.AddMinutes(5),
            now.AddMinutes(-15),
            "preflight_rejected",
            0m,
            8m,
            "test rejection",
            "{}",
            string.Empty,
            now.AddMinutes(-15),
            StrategyId: variant.Id));
        await repository.AddLiveOrderAsync(new LiveOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Rejected,
            null,
            TradeSide.Buy,
            "asset-live-clob-rejected",
            "condition-live-recent",
            "Down",
            0.50m,
            8m,
            4m,
            "GTC",
            now.AddMinutes(-12),
            now.AddMinutes(5),
            now.AddMinutes(-12),
            "rejected",
            0m,
            8m,
            "test clob rejection",
            "{}",
            string.Empty,
            now.AddMinutes(-12),
            StrategyId: variant.Id));

        var row = (await repository.GetStrategyRecentPerformanceAsync())
            .Single(item => item.StrategyId == variant.Id && item.Window == "1h");

        Assert.Equal(3, row.OrdersCount);
        Assert.Equal(1, row.FilledOrdersCount);
        Assert.Equal(1, row.ExpiredOrdersCount);
        Assert.Equal(1, row.OpenOrdersCount);
        Assert.Equal(1, row.EnteredRunsCount);
        Assert.Equal(1, row.SkippedRunsCount);
        Assert.Equal(1, row.SettledRunsCount);
        Assert.Equal(1, row.WonRunsCount);
        Assert.Equal(0, row.LostRunsCount);
        Assert.Equal(3m, row.FilledCostUsd);
        Assert.Equal(3m, row.RealizedPnlUsd);
        Assert.Equal(0.50m, row.AvgFillPrice);
        Assert.Equal(60m, row.AvgEntryDelaySeconds);
        Assert.Equal(60m, row.MaxEntryDelaySeconds);
        Assert.Equal(100m, row.WinRatePct);
        Assert.Equal(100m, row.RoiPct);
        Assert.Equal(1, row.LiveSettledOrdersCount);
        Assert.Equal(3, row.LiveSkippedOrdersCount);
        Assert.Equal(1, row.LiveConditionSkippedOrdersCount);
        Assert.Equal(1, row.LiveTechnicalSkippedOrdersCount);
        Assert.Equal(1, row.LiveRejectedOrdersCount);
        Assert.Equal(1, row.LiveWonOrdersCount);
        Assert.Equal(0, row.LiveLostOrdersCount);
        Assert.Equal(6m, row.LiveRealizedPnlUsd);
        Assert.Equal(150m, row.LiveRoiPct);
        Assert.Equal("btc_reference_move_below_bps_threshold:1", row.TopSkipReason);
    }

    private static void AddSettlement(
        TestAppRepository repository,
        DateTimeOffset settledAtUtc,
        string assetId,
        bool won,
        decimal realizedPnlUsd)
    {
        var costBasisUsd = won ? 5m : -realizedPnlUsd;
        repository.PaperPositionSettlements.Add(new PaperPositionSettlement(
            Guid.NewGuid(),
            "0xleader",
            assetId,
            "condition-" + assetId,
            "Yes",
            won ? assetId : "other-" + assetId,
            won ? "Yes" : "No",
            "Politics",
            10m,
            costBasisUsd / 10m,
            costBasisUsd,
            costBasisUsd + realizedPnlUsd,
            realizedPnlUsd,
            won,
            "test",
            settledAtUtc,
            settledAtUtc));
    }
}

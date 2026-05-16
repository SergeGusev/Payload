using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Tests;

public sealed class StrategyRunOrderingTests
{
    [Fact]
    public async Task GetDueStrategyMarketPaperRunsAsync_PrioritizesLiveOnlyWithinSameEntryDue()
    {
        var repository = new TestAppRepository();
        var paperStrategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var liveStrategyId = StrategyIds.BtcUpDown5mBinanceBps1;
        EnableLiveStakes(repository, liveStrategyId);

        var dueAtUtc = new DateTimeOffset(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);
        var earlierPaperRun = CreateRun(
            paperStrategyId,
            "earlier-paper",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-40),
            dueAtUtc.AddSeconds(-1));
        var paperTieRun = CreateRun(
            paperStrategyId,
            "paper-tie",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-30),
            dueAtUtc);
        var liveTieRun = CreateRun(
            liveStrategyId,
            "live-tie",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-10),
            dueAtUtc);
        repository.StrategyMarketPaperRuns.AddRange([paperTieRun, liveTieRun, earlierPaperRun]);

        var runs = await repository.GetDueStrategyMarketPaperRunsAsync(
            [paperStrategyId, liveStrategyId],
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(1),
            limit: 10);

        Assert.Equal(
            [earlierPaperRun.Id, liveTieRun.Id, paperTieRun.Id],
            runs.Select(run => run.Id));
    }

    [Fact]
    public async Task GetDueStrategyMarketPaperRunsAtEarliestDueAsync_PrioritizesLiveWithinEarliestDueBatch()
    {
        var repository = new TestAppRepository();
        var paperStrategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var liveStrategyId = StrategyIds.BtcUpDown5mBinanceBps1;
        EnableLiveStakes(repository, liveStrategyId);

        var dueAtUtc = new DateTimeOffset(2026, 5, 16, 12, 5, 0, TimeSpan.Zero);
        var paperTieRun = CreateRun(
            paperStrategyId,
            "paper-earliest",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-30),
            dueAtUtc);
        var liveTieRun = CreateRun(
            liveStrategyId,
            "live-earliest",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-10),
            dueAtUtc);
        var laterLiveRun = CreateRun(
            liveStrategyId,
            "later-live",
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(-5),
            dueAtUtc.AddSeconds(10));
        repository.StrategyMarketPaperRuns.AddRange([paperTieRun, laterLiveRun, liveTieRun]);

        var runs = await repository.GetDueStrategyMarketPaperRunsAtEarliestDueAsync(
            [paperStrategyId, liveStrategyId],
            StrategyMarketPaperRunStatuses.Observed,
            dueAtUtc.AddSeconds(20));

        Assert.Equal(
            [liveTieRun.Id, paperTieRun.Id],
            runs.Select(run => run.Id));
    }

    [Fact]
    public async Task GetPreOpenSellExitDueRunsAsync_PrioritizesLiveWhenExitTimeTies()
    {
        var repository = new TestAppRepository();
        var paperStrategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var liveStrategyId = StrategyIds.BtcUpDown5mBinanceBps1;
        EnableLiveStakes(repository, liveStrategyId);

        var marketStartUtc = new DateTimeOffset(2026, 5, 16, 12, 10, 0, TimeSpan.Zero);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var enteredAtUtc = marketStartUtc.AddSeconds(5);
        var dueBeforeUtc = marketStartUtc.AddMinutes(4);
        var paperTieRun = CreateRun(
            paperStrategyId,
            "paper-exit",
            StrategyMarketPaperRunStatuses.Entered,
            marketStartUtc,
            marketStartUtc,
            marketStartUtc,
            marketEndUtc,
            enteredAtUtc);
        var liveTieRun = CreateRun(
            liveStrategyId,
            "live-exit",
            StrategyMarketPaperRunStatuses.Entered,
            marketStartUtc.AddSeconds(10),
            marketStartUtc,
            marketStartUtc,
            marketEndUtc,
            enteredAtUtc);
        repository.StrategyMarketPaperRuns.AddRange([paperTieRun, liveTieRun]);

        var runs = await repository.GetPreOpenSellExitDueRunsAsync(
            [paperStrategyId, liveStrategyId],
            dueBeforeUtc,
            limit: 10);

        Assert.Equal(
            [liveTieRun.Id, paperTieRun.Id],
            runs.Select(run => run.Id));
    }

    [Fact]
    public async Task GetStrategyMarketPaperRunsForSettlementAsync_PrioritizesLiveWhenSettlementTimeTies()
    {
        var repository = new TestAppRepository();
        var paperStrategyId = StrategyIds.BtcUpDown5mBinanceBps2;
        var liveStrategyId = StrategyIds.BtcUpDown5mBinanceBps1;
        EnableLiveStakes(repository, liveStrategyId);

        var marketStartUtc = new DateTimeOffset(2026, 5, 16, 12, 15, 0, TimeSpan.Zero);
        var marketEndUtc = marketStartUtc.AddMinutes(5);
        var enteredAtUtc = marketStartUtc.AddSeconds(5);
        var paperTieRun = CreateRun(
            paperStrategyId,
            "paper-settle",
            StrategyMarketPaperRunStatuses.Entered,
            marketStartUtc,
            marketStartUtc,
            marketStartUtc,
            marketEndUtc,
            enteredAtUtc);
        var liveTieRun = CreateRun(
            liveStrategyId,
            "live-settle",
            StrategyMarketPaperRunStatuses.Entered,
            marketStartUtc.AddSeconds(10),
            marketStartUtc,
            marketStartUtc,
            marketEndUtc,
            enteredAtUtc);
        repository.StrategyMarketPaperRuns.AddRange([paperTieRun, liveTieRun]);

        var runs = await repository.GetStrategyMarketPaperRunsForSettlementAsync(
            [paperStrategyId, liveStrategyId],
            marketEndUtc.AddSeconds(1),
            limit: 10);

        Assert.Equal(
            [liveTieRun.Id, paperTieRun.Id],
            runs.Select(run => run.Id));
    }

    private static void EnableLiveStakes(TestAppRepository repository, Guid strategyId)
    {
        var normalizedStrategyId = StrategyIds.Normalize(strategyId);
        repository.StrategySettings[normalizedStrategyId] =
            StrategyRuntimeSettings.Default(normalizedStrategyId) with { LiveStakes = true };
    }

    private static StrategyMarketPaperRun CreateRun(
        Guid strategyId,
        string marketId,
        string status,
        DateTimeOffset detectedAtUtc,
        DateTimeOffset entryDueAtUtc,
        DateTimeOffset? marketStartUtc = null,
        DateTimeOffset? marketEndUtc = null,
        DateTimeOffset? enteredAtUtc = null)
    {
        return new StrategyMarketPaperRun(
            Guid.NewGuid(),
            strategyId,
            marketId,
            "condition-" + marketId,
            "btc-updown-5m-" + marketId,
            "BTC Up or Down 5m",
            "Crypto",
            marketStartUtc,
            marketEndUtc,
            detectedAtUtc,
            entryDueAtUtc,
            status,
            SelectedAssetId: null,
            SelectedOutcome: null,
            EntryPrice: null,
            StakeUsd: 1m,
            SizeShares: null,
            SignalId: null,
            PaperOrderId: null,
            EnteredAtUtc: enteredAtUtc,
            SettlementPrice: null,
            SettlementValueUsd: null,
            RealizedPnlUsd: null,
            SettledAtUtc: null,
            SkipReason: null,
            CreatedAtUtc: detectedAtUtc,
            UpdatedAtUtc: detectedAtUtc);
    }
}

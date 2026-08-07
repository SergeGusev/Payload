using PolyCopyTrader.Domain;
using PolyCopyTrader.Storage;

namespace PolyCopyTrader.Tests;

public sealed class DashboardProjectionCalculatorTests
{
    [Fact]
    public void StrategyRunStatusTransition_ReplacesPreviousContribution()
    {
        var strategyId = Guid.NewGuid();
        var nowUtc = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var observed = CreateRun(strategyId, nowUtc) with
        {
            Status = StrategyMarketPaperRunStatuses.Observed
        };
        var skipped = observed with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "reference_threshold_not_met",
            UpdatedAtUtc = nowUtc.AddSeconds(2)
        };
        var settled = skipped with
        {
            Status = StrategyMarketPaperRunStatuses.Settled,
            EnteredAtUtc = nowUtc.AddSeconds(1),
            RealizedPnlUsd = 2m,
            SettledAtUtc = nowUtc.AddSeconds(5),
            SkipReason = null,
            UpdatedAtUtc = nowUtc.AddSeconds(5)
        };
        var state = new DashboardLifetimeProjectionState();

        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(observed),
            1);
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(observed),
            -1);
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(skipped),
            1);

        Assert.Equal(1, state.RunsCount);
        Assert.Equal(0, state.ObservedRunsCount);
        Assert.Equal(1, state.SkippedRunsCount);
        Assert.Equal(1, state.PaperConditionSkippedRunsCount);
        Assert.Equal(1, state.RunLiveConditionSkippedCount);

        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(skipped),
            -1);
        DashboardProjectionCalculator.Apply(
            state,
            DashboardProjectionCalculator.GetLifetimeContribution(settled),
            1);

        Assert.Equal(1, state.RunsCount);
        Assert.Equal(0, state.SkippedRunsCount);
        Assert.Equal(1, state.SettledRunsCount);
        Assert.Equal(1, state.RunWonCount);
        Assert.Equal(2m, state.RunRealizedPnlUsd);
        Assert.Equal(1m, state.MaxEntryDelaySeconds);
        Assert.Equal(nowUtc.AddSeconds(5), state.LastRunUtc);
    }

    [Fact]
    public void PrepareFact_AppliesOnlyToWindowsContainingOccurrence()
    {
        var nowUtc = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var sourceFact = new DashboardRecentProjectionFact(
            DashboardProjectionSourceKinds.PaperOrder,
            Guid.NewGuid(),
            DashboardProjectionFactKinds.PaperOrderCreated,
            Guid.NewGuid(),
            nowUtc.AddHours(-2),
            new DashboardRecentContribution { OrdersCount = 1 },
            false,
            false,
            false);

        var prepared = PostgresDashboardProjectionRepository.PrepareFact(sourceFact, nowUtc);

        Assert.False(prepared.Applied1Hour);
        Assert.True(prepared.Applied6Hours);
        Assert.True(prepared.Applied24Hours);
    }

    [Fact]
    public void StrategyPaperSkipRollup_AddsOnlyPaperSkipLifetimeContribution()
    {
        var lastRunUtc = new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero);
        var payload = new StrategyPaperSkipRollupProjectionPayload(
            Guid.NewGuid(),
            17,
            lastRunUtc);

        var contribution = DashboardProjectionCalculator.GetLifetimeContribution(payload);

        Assert.Equal(17, contribution.RunsCount);
        Assert.Equal(17, contribution.SkippedRunsCount);
        Assert.Equal(17, contribution.PaperConditionSkippedRunsCount);
        Assert.Equal(0, contribution.PaperNotAcceptedRunsCount);
        Assert.Equal(0, contribution.RunLiveConditionSkippedCount);
        Assert.Equal(0, contribution.RunLiveTechnicalSkippedCount);
        Assert.Equal(0, contribution.RunLiveIgnoredGtdCount);
        Assert.Equal(lastRunUtc, contribution.LastRunUtc);
    }

    [Fact]
    public void LiveGrossStakeExcludesRecordedFeeFromRoiDenominator()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var payload = new LiveOrderProjectionPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            LiveOrderStatus.Matched.ToString(),
            0.50m,
            100m,
            0m,
            50m,
            51.75m,
            1.75m,
            100m,
            50m,
            nowUtc,
            true,
            nowUtc.AddMinutes(-5),
            nowUtc);

        var contribution = DashboardProjectionCalculator.GetLifetimeContribution(payload);
        var recentStake = DashboardProjectionCalculator.GetRecentFacts(payload)
            .Sum(fact => fact.Contribution.LiveStakeUsd);

        Assert.Equal(50m, contribution.LiveStakeUsd);
        Assert.Equal(50m, recentStake);
    }

    [Fact]
    public void PaperOnlySkipTombstoneFacts_UseExact24HourBoundaryAndNeverCountAsLive()
    {
        var nowUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = nowUtc.AddHours(-24);
        var payload = CreateRun(Guid.NewGuid(), updatedAtUtc) with
        {
            Status = StrategyMarketPaperRunStatuses.Skipped,
            SkipReason = "reference_threshold_not_met",
            LiveEnabledAtUtc = null
        };

        var facts = DashboardProjectionCalculator.GetRecentFacts(payload)
            .Select(fact => PostgresDashboardProjectionRepository.PrepareFact(fact, nowUtc))
            .ToArray();

        Assert.Equal(2, facts.Length);
        Assert.All(facts, fact =>
        {
            Assert.False(fact.Applied1Hour);
            Assert.False(fact.Applied6Hours);
            Assert.True(fact.Applied24Hours);
        });

        var skipped = Assert.Single(facts, fact =>
            fact.FactKind == DashboardProjectionFactKinds.RunSkipped);
        Assert.Equal(1, skipped.Contribution.SkippedRunsCount);
        Assert.Equal(1, skipped.Contribution.PaperConditionSkippedRunsCount);
        Assert.Equal(0, skipped.Contribution.PaperNotAcceptedRunsCount);
        Assert.Equal(0, skipped.Contribution.RunLiveConditionSkippedCount);
        Assert.Equal(0, skipped.Contribution.RunLiveTechnicalSkippedCount);
        Assert.Equal(0, skipped.Contribution.RunLiveIgnoredGtdCount);
        Assert.Equal("reference_threshold_not_met", skipped.Contribution.SkipReason);

        var activity = Assert.Single(facts, fact =>
            fact.FactKind == DashboardProjectionFactKinds.RunActivity);
        Assert.Equal(updatedAtUtc, activity.Contribution.LastRunUtc);

        var outsideBoundary = DashboardProjectionCalculator.GetRecentFacts(
                payload with { UpdatedAtUtc = updatedAtUtc.AddTicks(-1) })
            .Select(fact => PostgresDashboardProjectionRepository.PrepareFact(fact, nowUtc))
            .ToArray();
        Assert.All(outsideBoundary, fact => Assert.False(fact.Applied24Hours));
    }

    [Fact]
    public void RecentCandidateRemoval_CanBeRebuiltFromRemainingFacts()
    {
        var state = new DashboardRecentProjectionState();
        var maximum = new DashboardRecentContribution
        {
            EntryDelayCandidateSeconds = 5m,
            EntryDelayTotalSeconds = 5m,
            EntryDelayCount = 1
        };
        var remaining = new DashboardRecentContribution
        {
            EntryDelayCandidateSeconds = 2m,
            EntryDelayTotalSeconds = 2m,
            EntryDelayCount = 1
        };
        DashboardProjectionCalculator.Apply(state, maximum, 1);
        DashboardProjectionCalculator.Apply(state, remaining, 1);

        var rebuildRequired = DashboardProjectionCalculator.Apply(state, maximum, -1);
        DashboardProjectionCalculator.RebuildRecentCandidates(state, [remaining]);

        Assert.True(rebuildRequired);
        Assert.Equal(2m, state.MaxEntryDelaySeconds);
        Assert.Equal(2m, state.EntryDelayTotalSeconds);
        Assert.Equal(1, state.EntryDelayCount);
    }

    [Fact]
    public void ProjectionJson_NormalizesDateTimeOffsetsToUtc()
    {
        var localTimestamp = new DateTimeOffset(2026, 7, 10, 11, 30, 0, TimeSpan.FromHours(3));
        var payload = CreateRun(Guid.NewGuid(), localTimestamp) with
        {
            UpdatedAtUtc = localTimestamp
        };

        var json = PostgresDashboardProjectionRepository.Serialize(payload);
        var roundTrip = PostgresDashboardProjectionRepository.Deserialize<StrategyRunProjectionPayload>(json);

        Assert.Equal(TimeSpan.Zero, roundTrip.EntryDueAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, roundTrip.UpdatedAtUtc.Offset);
        Assert.Equal(localTimestamp.UtcDateTime, roundTrip.UpdatedAtUtc.UtcDateTime);
    }

    private static StrategyRunProjectionPayload CreateRun(
        Guid strategyId,
        DateTimeOffset nowUtc)
    {
        return new StrategyRunProjectionPayload(
            Guid.NewGuid(),
            strategyId,
            StrategyMarketPaperRunStatuses.Observed,
            6m,
            null,
            nowUtc,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc.AddHours(-1));
    }
}

using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Storage;

public static class DashboardProjectionFactKinds
{
    public const string PaperOrderCreated = "PaperOrderCreated";
    public const string PaperFill = "PaperFill";
    public const string RunActivity = "RunActivity";
    public const string RunEntered = "RunEntered";
    public const string RunSkipped = "RunSkipped";
    public const string RunSettled = "RunSettled";
    public const string LiveOrderCreated = "LiveOrderCreated";
    public const string LiveOrderSettled = "LiveOrderSettled";
}

public static class DashboardProjectionCalculator
{
    private static readonly HashSet<string> ConditionSkipReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "btc_reference_move_below_bps_threshold",
        "btc_reference_equal_market_start",
        "btc_reference_equal_mean",
        "btc_reference_mixed_around_mean",
        "btc_market_results_not_consecutive",
        "btc_previous_score_countertrend_rejected",
        "btc_previous_score_neutral",
        "btc_previous_score_down_time_share_below_threshold",
        "btc_previous_score_up_time_share_below_threshold",
        "btc_clever_fair_value_below_margin",
        "btc_clever_fair_value_rejected",
        "markov_edge_below_threshold",
        "martin_not_triggered",
        "strategy_selector_no_candidate_current_entry",
        "gtd_limit_decision_rejected"
    };

    private static readonly string[] ConditionSkipFragments =
    [
        "threshold",
        "edge",
        "countertrend",
        "neutral",
        "not_triggered",
        "no_candidate",
        "spread_too_wide",
        "price_cap"
    ];

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperOrderProjectionPayload payload)
    {
        var filled = IsFilledPaperOrderStatus(payload.Status);
        var open = IsOpenPaperOrderStatus(payload.Status);
        var previousScoreBps = payload.PreviousScoreBps ?? payload.PreviousScore * 10_000m;
        decimal? signalBps = previousScoreBps is null
            ? null
            : payload.SelectedSignalBps ?? Math.Abs(previousScoreBps.Value);

        return new DashboardLifetimeContribution
        {
            OrdersCount = 1,
            FilledOrdersCount = filled ? 1 : 0,
            OpenOrdersCount = open ? 1 : 0,
            BuyNotionalUsd = filled && IsBuy(payload.Side) ? payload.NotionalUsd : 0m,
            CountertrendScoreSumBps = previousScoreBps ?? 0m,
            CountertrendScoreCount = previousScoreBps is null ? 0 : 1,
            CountertrendSignalSumBps = signalBps ?? 0m,
            CountertrendSignalCount = signalBps is null ? 0 : 1,
            LastCountertrendSignalBps = signalBps,
            LastCountertrendSignalAtUtc = signalBps is null ? null : payload.CreatedAtUtc,
            LastOrderUtc = payload.CreatedAtUtc
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperFillProjectionPayload payload)
    {
        return new DashboardLifetimeContribution
        {
            FillRealizedPnlUsd = payload.RealizedPnlUsd,
            FillClosedCostBasisUsd = IsSell(payload.OrderSide)
                ? (payload.Price * payload.SizeShares) - payload.RealizedPnlUsd
                : 0m
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(StrategyRunProjectionPayload payload)
    {
        var realizedPnl = payload.RealizedPnlUsd ?? 0m;
        var isObserved = IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Observed);
        var isEntered = IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Entered);
        var isSkipped = IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Skipped);
        var isSettled = IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Settled);
        var isWin = isSettled && realizedPnl > 0m;
        var isLoss = isSettled && realizedPnl < 0m;
        var delay = GetEntryDelaySeconds(payload);
        var skipKind = ClassifyLiveSkip(payload);

        return new DashboardLifetimeContribution
        {
            RunsCount = 1,
            ObservedRunsCount = isObserved ? 1 : 0,
            EnteredRunsCount = isEntered ? 1 : 0,
            SkippedRunsCount = isSkipped ? 1 : 0,
            PaperConditionSkippedRunsCount = isSkipped && payload.PaperOrderId is null ? 1 : 0,
            PaperNotAcceptedRunsCount = isSkipped && payload.PaperOrderId is not null ? 1 : 0,
            SettledRunsCount = isSettled ? 1 : 0,
            RunWonCount = isWin ? 1 : 0,
            RunLostCount = isLoss ? 1 : 0,
            RunSettledStakeUsd = isSettled ? payload.StakeUsd : 0m,
            RunRealizedPnlUsd = isSettled ? realizedPnl : 0m,
            RunWinPnlSumUsd = isWin ? realizedPnl : 0m,
            RunWinCount = isWin ? 1 : 0,
            RunLossPnlSumUsd = isLoss ? realizedPnl : 0m,
            RunLossCount = isLoss ? 1 : 0,
            RunPositivePnlUsd = isWin ? realizedPnl : 0m,
            RunLossAbsPnlUsd = isLoss ? -realizedPnl : 0m,
            EntryDelayTotalSeconds = delay ?? 0m,
            EntryDelayCount = delay is null ? 0 : 1,
            MaxEntryDelaySeconds = delay ?? 0m,
            RunLiveConditionSkippedCount = skipKind == DashboardLiveSkipKind.Condition ? 1 : 0,
            RunLiveTechnicalSkippedCount = skipKind == DashboardLiveSkipKind.Technical ? 1 : 0,
            RunLiveIgnoredGtdCount = skipKind == DashboardLiveSkipKind.IgnoredGtd ? 1 : 0,
            LastRunUtc = payload.UpdatedAtUtc
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperPositionProjectionPayload payload)
    {
        return new DashboardLifetimeContribution
        {
            OpenPositionsCount = payload.SizeShares > 0m ? 1 : 0,
            UnrealizedPnlUsd = payload.SizeShares > 0m ? payload.UnrealizedPnlUsd : 0m
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperSettlementProjectionPayload payload)
    {
        return new DashboardLifetimeContribution
        {
            SettlementCount = 1,
            SettlementWonCount = payload.Won ? 1 : 0,
            SettlementLostCount = payload.Won ? 0 : 1,
            SettlementCostBasisUsd = payload.CostBasisUsd,
            SettlementRealizedPnlUsd = payload.RealizedPnlUsd,
            SettlementWinPnlSumUsd = payload.Won ? payload.RealizedPnlUsd : 0m,
            SettlementWinCount = payload.Won ? 1 : 0,
            SettlementLossPnlSumUsd = payload.Won ? 0m : payload.RealizedPnlUsd,
            SettlementLossCount = payload.Won ? 0 : 1,
            SettlementPositivePnlUsd = payload.RealizedPnlUsd > 0m ? payload.RealizedPnlUsd : 0m,
            SettlementLossAbsPnlUsd = payload.RealizedPnlUsd < 0m ? -payload.RealizedPnlUsd : 0m
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(LiveOrderProjectionPayload payload)
    {
        var settled = payload.SettledAtUtc is not null;
        var countedAsSettled = settled && payload.RealizedPnlUsd is not null;
        var won = settled && (payload.Won ?? (payload.SettlementValueUsd ?? 0m) > 0m);
        var lost = settled && !won;
        var realizedPnl = settled ? payload.RealizedPnlUsd ?? 0m : 0m;
        var stake = settled ? GetLiveStakeUsd(payload) : 0m;

        return new DashboardLifetimeContribution
        {
            LiveOrdersCount = 1,
            LiveFilledOrdersCount = payload.FilledSize > 0m ? 1 : 0,
            LiveOpenOrdersCount = IsOpenLiveOrderStatus(payload.Status) && payload.RemainingSize > 0m ? 1 : 0,
            LiveSettledOrdersCount = countedAsSettled ? 1 : 0,
            LiveTechnicalSkippedCount = IsStatus(payload.Status, nameof(LiveOrderStatus.PreflightRejected)) ? 1 : 0,
            LiveIgnoredCancelledCount = IsIgnoredCancelledLiveOrder(payload) ? 1 : 0,
            LiveIgnoredRejectedCount = IsIgnoredRejectedLiveOrder(payload.Status) ? 1 : 0,
            LiveWonCount = won ? 1 : 0,
            LiveLostCount = lost ? 1 : 0,
            LiveStakeUsd = stake,
            LiveRealizedPnlUsd = realizedPnl,
            LiveWinPnlSumUsd = won ? realizedPnl : 0m,
            LiveWinCount = won ? 1 : 0,
            LiveLossPnlSumUsd = lost ? realizedPnl : 0m,
            LiveLossCount = lost ? 1 : 0,
            LivePositivePnlUsd = realizedPnl > 0m ? realizedPnl : 0m,
            LiveLossAbsPnlUsd = realizedPnl < 0m ? -realizedPnl : 0m,
            LiveLastOrderUtc = payload.CreatedAtUtc,
            LiveLastSettlementUtc = payload.SettledAtUtc
        };
    }

    public static IReadOnlyList<DashboardRecentProjectionFact> GetRecentFacts(PaperOrderProjectionPayload payload)
    {
        return
        [
            CreateFact(
                DashboardProjectionSourceKinds.PaperOrder,
                payload.Id,
                DashboardProjectionFactKinds.PaperOrderCreated,
                payload.StrategyId,
                payload.CreatedAtUtc,
                new DashboardRecentContribution
                {
                    OrdersCount = 1,
                    FilledOrdersCount = IsFilledPaperOrderStatus(payload.Status) ? 1 : 0,
                    ExpiredOrdersCount = IsExpiredPaperOrderStatus(payload.Status) ? 1 : 0,
                    OpenOrdersCount = IsOpenPaperOrderStatus(payload.Status) ? 1 : 0,
                    LastOrderUtc = payload.CreatedAtUtc
                })
        ];
    }

    public static IReadOnlyList<DashboardRecentProjectionFact> GetRecentFacts(PaperFillProjectionPayload payload)
    {
        return
        [
            CreateFact(
                DashboardProjectionSourceKinds.PaperFill,
                payload.Id,
                DashboardProjectionFactKinds.PaperFill,
                payload.StrategyId,
                payload.FilledAtUtc,
                new DashboardRecentContribution
                {
                    FilledCostUsd = payload.Price * payload.SizeShares,
                    FilledSizeShares = payload.SizeShares
                })
        ];
    }

    public static IReadOnlyList<DashboardRecentProjectionFact> GetRecentFacts(StrategyRunProjectionPayload payload)
    {
        var facts = new List<DashboardRecentProjectionFact>(4)
        {
            CreateFact(
                DashboardProjectionSourceKinds.StrategyRun,
                payload.Id,
                DashboardProjectionFactKinds.RunActivity,
                payload.StrategyId,
                payload.UpdatedAtUtc,
                new DashboardRecentContribution { LastRunUtc = payload.UpdatedAtUtc })
        };

        var delay = GetEntryDelaySeconds(payload);
        if (payload.EnteredAtUtc is not null)
        {
            facts.Add(CreateFact(
                DashboardProjectionSourceKinds.StrategyRun,
                payload.Id,
                DashboardProjectionFactKinds.RunEntered,
                payload.StrategyId,
                payload.EnteredAtUtc.Value,
                new DashboardRecentContribution
                {
                    EnteredRunsCount = 1,
                    EntryDelayTotalSeconds = delay ?? 0m,
                    EntryDelayCount = delay is null ? 0 : 1,
                    EntryDelayCandidateSeconds = delay
                }));
        }

        if (IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Skipped))
        {
            var skipKind = ClassifyLiveSkip(payload);
            facts.Add(CreateFact(
                DashboardProjectionSourceKinds.StrategyRun,
                payload.Id,
                DashboardProjectionFactKinds.RunSkipped,
                payload.StrategyId,
                payload.UpdatedAtUtc,
                new DashboardRecentContribution
                {
                    SkippedRunsCount = 1,
                    PaperConditionSkippedRunsCount = payload.PaperOrderId is null ? 1 : 0,
                    PaperNotAcceptedRunsCount = payload.PaperOrderId is null ? 0 : 1,
                    RunLiveConditionSkippedCount = skipKind == DashboardLiveSkipKind.Condition ? 1 : 0,
                    RunLiveTechnicalSkippedCount = skipKind == DashboardLiveSkipKind.Technical ? 1 : 0,
                    RunLiveIgnoredGtdCount = skipKind == DashboardLiveSkipKind.IgnoredGtd ? 1 : 0,
                    SkipReason = string.IsNullOrWhiteSpace(payload.SkipReason) ? null : payload.SkipReason
                }));
        }

        if (IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Settled) && payload.SettledAtUtc is not null)
        {
            var realizedPnl = payload.RealizedPnlUsd ?? 0m;
            facts.Add(CreateFact(
                DashboardProjectionSourceKinds.StrategyRun,
                payload.Id,
                DashboardProjectionFactKinds.RunSettled,
                payload.StrategyId,
                payload.SettledAtUtc.Value,
                new DashboardRecentContribution
                {
                    SettledRunsCount = 1,
                    WonRunsCount = realizedPnl > 0m ? 1 : 0,
                    LostRunsCount = realizedPnl < 0m ? 1 : 0,
                    SettledStakeUsd = payload.StakeUsd,
                    RealizedPnlUsd = realizedPnl
                }));
        }

        return facts;
    }

    public static IReadOnlyList<DashboardRecentProjectionFact> GetRecentFacts(LiveOrderProjectionPayload payload)
    {
        var facts = new List<DashboardRecentProjectionFact>(2)
        {
            CreateFact(
                DashboardProjectionSourceKinds.LiveOrder,
                payload.Id,
                DashboardProjectionFactKinds.LiveOrderCreated,
                payload.StrategyId,
                payload.CreatedAtUtc,
                new DashboardRecentContribution
                {
                    LiveTechnicalSkippedCount = IsStatus(payload.Status, nameof(LiveOrderStatus.PreflightRejected)) ? 1 : 0,
                    LiveIgnoredCancelledCount = IsIgnoredCancelledLiveOrder(payload) ? 1 : 0,
                    LiveIgnoredRejectedCount = IsIgnoredRejectedLiveOrder(payload.Status) ? 1 : 0
                })
        };

        if (payload.SettledAtUtc is not null)
        {
            var won = payload.Won ?? (payload.SettlementValueUsd ?? 0m) > 0m;
            facts.Add(CreateFact(
                DashboardProjectionSourceKinds.LiveOrder,
                payload.Id,
                DashboardProjectionFactKinds.LiveOrderSettled,
                payload.StrategyId,
                payload.SettledAtUtc.Value,
                new DashboardRecentContribution
                {
                    LiveSettledOrdersCount = payload.RealizedPnlUsd is null ? 0 : 1,
                    LiveWonCount = won ? 1 : 0,
                    LiveLostCount = won ? 0 : 1,
                    LiveStakeUsd = GetLiveStakeUsd(payload),
                    LiveRealizedPnlUsd = payload.RealizedPnlUsd ?? 0m
                }));
        }

        return facts;
    }

    public static void Apply(DashboardLifetimeProjectionState state, DashboardLifetimeContribution contribution, int sign)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sign);
        if (sign is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(sign));
        }

        state.OrdersCount += sign * contribution.OrdersCount;
        state.FilledOrdersCount += sign * contribution.FilledOrdersCount;
        state.OpenOrdersCount += sign * contribution.OpenOrdersCount;
        state.BuyNotionalUsd += sign * contribution.BuyNotionalUsd;
        state.CountertrendScoreSumBps += sign * contribution.CountertrendScoreSumBps;
        state.CountertrendScoreCount += sign * contribution.CountertrendScoreCount;
        state.CountertrendSignalSumBps += sign * contribution.CountertrendSignalSumBps;
        state.CountertrendSignalCount += sign * contribution.CountertrendSignalCount;
        state.FillRealizedPnlUsd += sign * contribution.FillRealizedPnlUsd;
        state.FillClosedCostBasisUsd += sign * contribution.FillClosedCostBasisUsd;
        state.OpenPositionsCount += sign * contribution.OpenPositionsCount;
        state.UnrealizedPnlUsd += sign * contribution.UnrealizedPnlUsd;
        state.SettlementCount += sign * contribution.SettlementCount;
        state.SettlementWonCount += sign * contribution.SettlementWonCount;
        state.SettlementLostCount += sign * contribution.SettlementLostCount;
        state.SettlementCostBasisUsd += sign * contribution.SettlementCostBasisUsd;
        state.SettlementRealizedPnlUsd += sign * contribution.SettlementRealizedPnlUsd;
        state.SettlementWinPnlSumUsd += sign * contribution.SettlementWinPnlSumUsd;
        state.SettlementWinCount += sign * contribution.SettlementWinCount;
        state.SettlementLossPnlSumUsd += sign * contribution.SettlementLossPnlSumUsd;
        state.SettlementLossCount += sign * contribution.SettlementLossCount;
        state.SettlementPositivePnlUsd += sign * contribution.SettlementPositivePnlUsd;
        state.SettlementLossAbsPnlUsd += sign * contribution.SettlementLossAbsPnlUsd;
        state.RunsCount += sign * contribution.RunsCount;
        state.ObservedRunsCount += sign * contribution.ObservedRunsCount;
        state.EnteredRunsCount += sign * contribution.EnteredRunsCount;
        state.SkippedRunsCount += sign * contribution.SkippedRunsCount;
        state.PaperConditionSkippedRunsCount += sign * contribution.PaperConditionSkippedRunsCount;
        state.PaperNotAcceptedRunsCount += sign * contribution.PaperNotAcceptedRunsCount;
        state.SettledRunsCount += sign * contribution.SettledRunsCount;
        state.RunWonCount += sign * contribution.RunWonCount;
        state.RunLostCount += sign * contribution.RunLostCount;
        state.RunSettledStakeUsd += sign * contribution.RunSettledStakeUsd;
        state.RunRealizedPnlUsd += sign * contribution.RunRealizedPnlUsd;
        state.RunWinPnlSumUsd += sign * contribution.RunWinPnlSumUsd;
        state.RunWinCount += sign * contribution.RunWinCount;
        state.RunLossPnlSumUsd += sign * contribution.RunLossPnlSumUsd;
        state.RunLossCount += sign * contribution.RunLossCount;
        state.RunPositivePnlUsd += sign * contribution.RunPositivePnlUsd;
        state.RunLossAbsPnlUsd += sign * contribution.RunLossAbsPnlUsd;
        state.EntryDelayTotalSeconds += sign * contribution.EntryDelayTotalSeconds;
        state.EntryDelayCount += sign * contribution.EntryDelayCount;
        state.RunLiveConditionSkippedCount += sign * contribution.RunLiveConditionSkippedCount;
        state.RunLiveTechnicalSkippedCount += sign * contribution.RunLiveTechnicalSkippedCount;
        state.RunLiveIgnoredGtdCount += sign * contribution.RunLiveIgnoredGtdCount;
        state.LiveOrdersCount += sign * contribution.LiveOrdersCount;
        state.LiveFilledOrdersCount += sign * contribution.LiveFilledOrdersCount;
        state.LiveOpenOrdersCount += sign * contribution.LiveOpenOrdersCount;
        state.LiveSettledOrdersCount += sign * contribution.LiveSettledOrdersCount;
        state.LiveTechnicalSkippedCount += sign * contribution.LiveTechnicalSkippedCount;
        state.LiveIgnoredCancelledCount += sign * contribution.LiveIgnoredCancelledCount;
        state.LiveIgnoredRejectedCount += sign * contribution.LiveIgnoredRejectedCount;
        state.LiveWonCount += sign * contribution.LiveWonCount;
        state.LiveLostCount += sign * contribution.LiveLostCount;
        state.LiveStakeUsd += sign * contribution.LiveStakeUsd;
        state.LiveRealizedPnlUsd += sign * contribution.LiveRealizedPnlUsd;
        state.LiveWinPnlSumUsd += sign * contribution.LiveWinPnlSumUsd;
        state.LiveWinCount += sign * contribution.LiveWinCount;
        state.LiveLossPnlSumUsd += sign * contribution.LiveLossPnlSumUsd;
        state.LiveLossCount += sign * contribution.LiveLossCount;
        state.LivePositivePnlUsd += sign * contribution.LivePositivePnlUsd;
        state.LiveLossAbsPnlUsd += sign * contribution.LiveLossAbsPnlUsd;

        if (sign > 0)
        {
            state.MaxEntryDelaySeconds = Math.Max(state.MaxEntryDelaySeconds, contribution.MaxEntryDelaySeconds);
            state.LastOrderUtc = Latest(state.LastOrderUtc, contribution.LastOrderUtc);
            state.LastRunUtc = Latest(state.LastRunUtc, contribution.LastRunUtc);
            state.LiveLastOrderUtc = Latest(state.LiveLastOrderUtc, contribution.LiveLastOrderUtc);
            state.LiveLastSettlementUtc = Latest(state.LiveLastSettlementUtc, contribution.LiveLastSettlementUtc);
            if (contribution.LastCountertrendSignalAtUtc is not null &&
                (state.LastCountertrendSignalAtUtc is null || contribution.LastCountertrendSignalAtUtc >= state.LastCountertrendSignalAtUtc))
            {
                state.LastCountertrendSignalAtUtc = contribution.LastCountertrendSignalAtUtc;
                state.LastCountertrendSignalBps = contribution.LastCountertrendSignalBps;
            }
        }

        ValidateNonnegativeCounts(state);
    }

    public static bool RequiresLifetimeCandidateRebuild(
        DashboardLifetimeProjectionState state,
        DashboardLifetimeContribution oldContribution,
        DashboardLifetimeContribution? newContribution)
    {
        return IsRemovedMaximum(oldContribution.MaxEntryDelaySeconds, newContribution?.MaxEntryDelaySeconds, state.MaxEntryDelaySeconds) ||
               IsRemovedLatest(oldContribution.LastOrderUtc, newContribution?.LastOrderUtc, state.LastOrderUtc) ||
               IsRemovedLatest(oldContribution.LastRunUtc, newContribution?.LastRunUtc, state.LastRunUtc) ||
               IsRemovedLatest(oldContribution.LiveLastOrderUtc, newContribution?.LiveLastOrderUtc, state.LiveLastOrderUtc) ||
               IsRemovedLatest(oldContribution.LiveLastSettlementUtc, newContribution?.LiveLastSettlementUtc, state.LiveLastSettlementUtc) ||
               IsRemovedLatest(oldContribution.LastCountertrendSignalAtUtc, newContribution?.LastCountertrendSignalAtUtc, state.LastCountertrendSignalAtUtc);
    }

    public static bool Apply(DashboardRecentProjectionState state, DashboardRecentContribution contribution, int sign)
    {
        if (sign is not (1 or -1))
        {
            throw new ArgumentOutOfRangeException(nameof(sign));
        }

        var rebuildCandidates = false;
        state.OrdersCount += sign * contribution.OrdersCount;
        state.FilledOrdersCount += sign * contribution.FilledOrdersCount;
        state.ExpiredOrdersCount += sign * contribution.ExpiredOrdersCount;
        state.OpenOrdersCount += sign * contribution.OpenOrdersCount;
        state.FilledCostUsd += sign * contribution.FilledCostUsd;
        state.FilledSizeShares += sign * contribution.FilledSizeShares;
        state.EnteredRunsCount += sign * contribution.EnteredRunsCount;
        state.SkippedRunsCount += sign * contribution.SkippedRunsCount;
        state.PaperConditionSkippedRunsCount += sign * contribution.PaperConditionSkippedRunsCount;
        state.PaperNotAcceptedRunsCount += sign * contribution.PaperNotAcceptedRunsCount;
        state.RunLiveConditionSkippedCount += sign * contribution.RunLiveConditionSkippedCount;
        state.RunLiveTechnicalSkippedCount += sign * contribution.RunLiveTechnicalSkippedCount;
        state.RunLiveIgnoredGtdCount += sign * contribution.RunLiveIgnoredGtdCount;
        state.SettledRunsCount += sign * contribution.SettledRunsCount;
        state.WonRunsCount += sign * contribution.WonRunsCount;
        state.LostRunsCount += sign * contribution.LostRunsCount;
        state.SettledStakeUsd += sign * contribution.SettledStakeUsd;
        state.RealizedPnlUsd += sign * contribution.RealizedPnlUsd;
        state.EntryDelayTotalSeconds += sign * contribution.EntryDelayTotalSeconds;
        state.EntryDelayCount += sign * contribution.EntryDelayCount;
        state.LiveSettledOrdersCount += sign * contribution.LiveSettledOrdersCount;
        state.LiveTechnicalSkippedCount += sign * contribution.LiveTechnicalSkippedCount;
        state.LiveIgnoredCancelledCount += sign * contribution.LiveIgnoredCancelledCount;
        state.LiveIgnoredRejectedCount += sign * contribution.LiveIgnoredRejectedCount;
        state.LiveWonCount += sign * contribution.LiveWonCount;
        state.LiveLostCount += sign * contribution.LiveLostCount;
        state.LiveStakeUsd += sign * contribution.LiveStakeUsd;
        state.LiveRealizedPnlUsd += sign * contribution.LiveRealizedPnlUsd;

        if (!string.IsNullOrWhiteSpace(contribution.SkipReason))
        {
            state.SkipReasonCounts.TryGetValue(contribution.SkipReason, out var currentCount);
            var nextCount = currentCount + sign * Math.Max(1, contribution.SkippedRunsCount);
            if (nextCount <= 0)
            {
                state.SkipReasonCounts.Remove(contribution.SkipReason);
            }
            else
            {
                state.SkipReasonCounts[contribution.SkipReason] = nextCount;
            }
        }

        if (sign > 0)
        {
            state.MaxEntryDelaySeconds = Math.Max(
                state.MaxEntryDelaySeconds,
                contribution.EntryDelayCandidateSeconds ?? 0m);
            state.LastOrderUtc = Latest(state.LastOrderUtc, contribution.LastOrderUtc);
            state.LastRunUtc = Latest(state.LastRunUtc, contribution.LastRunUtc);
        }
        else
        {
            rebuildCandidates =
                contribution.EntryDelayCandidateSeconds == state.MaxEntryDelaySeconds ||
                contribution.LastOrderUtc == state.LastOrderUtc ||
                contribution.LastRunUtc == state.LastRunUtc;
        }

        ValidateNonnegativeCounts(state);
        return rebuildCandidates;
    }

    public static bool RequiresRecentCandidateRebuild(
        DashboardRecentProjectionState state,
        IReadOnlyList<DashboardRecentProjectionFact> oldFacts,
        IReadOnlyList<DashboardRecentProjectionFact> newFacts)
    {
        var oldMaxDelay = oldFacts
            .Select(fact => fact.Contribution.EntryDelayCandidateSeconds ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
        var newMaxDelay = newFacts
            .Select(fact => fact.Contribution.EntryDelayCandidateSeconds ?? 0m)
            .DefaultIfEmpty(0m)
            .Max();
        var oldLastOrder = oldFacts
            .Select(fact => fact.Contribution.LastOrderUtc)
            .Where(value => value is not null)
            .Max();
        var newLastOrder = newFacts
            .Select(fact => fact.Contribution.LastOrderUtc)
            .Where(value => value is not null)
            .Max();
        var oldLastRun = oldFacts
            .Select(fact => fact.Contribution.LastRunUtc)
            .Where(value => value is not null)
            .Max();
        var newLastRun = newFacts
            .Select(fact => fact.Contribution.LastRunUtc)
            .Where(value => value is not null)
            .Max();

        return IsRemovedMaximum(oldMaxDelay, newMaxDelay, state.MaxEntryDelaySeconds) ||
               IsRemovedLatest(oldLastOrder, newLastOrder, state.LastOrderUtc) ||
               IsRemovedLatest(oldLastRun, newLastRun, state.LastRunUtc);
    }

    public static void RebuildRecentCandidates(
        DashboardRecentProjectionState state,
        IEnumerable<DashboardRecentContribution> activeContributions)
    {
        state.MaxEntryDelaySeconds = 0m;
        state.LastOrderUtc = null;
        state.LastRunUtc = null;
        foreach (var contribution in activeContributions)
        {
            state.MaxEntryDelaySeconds = Math.Max(
                state.MaxEntryDelaySeconds,
                contribution.EntryDelayCandidateSeconds ?? 0m);
            state.LastOrderUtc = Latest(state.LastOrderUtc, contribution.LastOrderUtc);
            state.LastRunUtc = Latest(state.LastRunUtc, contribution.LastRunUtc);
        }
    }

    public static StrategyPerformance ToStrategyPerformance(
        DashboardStrategyDescriptor strategy,
        DashboardLifetimeProjectionState state,
        DateTimeOffset nowUtc)
    {
        var usesRuns = state.RunsCount > 0;
        var settledPositions = usesRuns ? state.SettledRunsCount : state.SettlementCount;
        var wonPositions = usesRuns ? state.RunWonCount : state.SettlementWonCount;
        var lostPositions = usesRuns ? state.RunLostCount : state.SettlementLostCount;
        var stakeUsd = state.BuyNotionalUsd > 0m
            ? state.BuyNotionalUsd
            : state.RunSettledStakeUsd > 0m
                ? state.RunSettledStakeUsd
                : state.SettlementCostBasisUsd;
        var closedStakeUsd = usesRuns
            ? state.RunSettledStakeUsd
            : state.SettlementCostBasisUsd + state.FillClosedCostBasisUsd;
        var realizedPnlUsd = usesRuns
            ? state.RunRealizedPnlUsd
            : state.SettlementRealizedPnlUsd + state.FillRealizedPnlUsd;
        var avgWinPnlUsd = usesRuns
            ? Average(state.RunWinPnlSumUsd, state.RunWinCount)
            : Average(state.SettlementWinPnlSumUsd, state.SettlementWinCount);
        var avgLossPnlUsd = usesRuns
            ? Average(state.RunLossPnlSumUsd, state.RunLossCount)
            : Average(state.SettlementLossPnlSumUsd, state.SettlementLossCount);
        var positivePnlUsd = usesRuns ? state.RunPositivePnlUsd : state.SettlementPositivePnlUsd;
        var lossAbsPnlUsd = usesRuns ? state.RunLossAbsPnlUsd : state.SettlementLossAbsPnlUsd;
        var expectancyPnlUsd = usesRuns
            ? Average(state.RunRealizedPnlUsd, state.SettledRunsCount)
            : Average(state.SettlementRealizedPnlUsd, state.SettlementCount);
        var totalPnlUsd = realizedPnlUsd + state.UnrealizedPnlUsd;
        var runConditionSkips = strategy.LiveStakes ? state.RunLiveConditionSkippedCount : 0;
        var runTechnicalSkips = strategy.LiveStakes ? state.RunLiveTechnicalSkippedCount : 0;
        var runIgnoredGtd = strategy.LiveStakes ? state.RunLiveIgnoredGtdCount : 0;
        var liveTechnical = runTechnicalSkips + state.LiveTechnicalSkippedCount;
        var liveIgnored = runIgnoredGtd + state.LiveIgnoredCancelledCount + state.LiveIgnoredRejectedCount;
        var liveSkipped = runConditionSkips + liveTechnical + liveIgnored;
        var paused = strategy.Paused && (strategy.PausedUntilUtc is null || strategy.PausedUntilUtc > nowUtc);

        return new StrategyPerformance(
            strategy.StrategyId,
            strategy.Code,
            strategy.Name,
            strategy.Enabled,
            strategy.LiveStakes,
            paused,
            paused ? strategy.PausedUntilUtc : null,
            strategy.PaperStakeAmount,
            strategy.LiveStakeAmount,
            strategy.PaperLostCoeff,
            strategy.LiveLostCoeff,
            strategy.PaperLostCounter,
            strategy.LiveLostCounter,
            strategy.LiveAvailableBalance,
            state.OrdersCount,
            state.FilledOrdersCount,
            state.OpenOrdersCount,
            state.OpenPositionsCount,
            state.ObservedRunsCount,
            state.EnteredRunsCount,
            state.SkippedRunsCount,
            state.PaperConditionSkippedRunsCount,
            state.PaperNotAcceptedRunsCount,
            state.SettledRunsCount,
            settledPositions,
            wonPositions,
            lostPositions,
            stakeUsd,
            realizedPnlUsd,
            state.UnrealizedPnlUsd,
            totalPnlUsd,
            Percent(wonPositions, settledPositions),
            Percent(lostPositions, settledPositions),
            avgWinPnlUsd,
            avgLossPnlUsd,
            lossAbsPnlUsd == 0m ? null : positivePnlUsd / lossAbsPnlUsd,
            expectancyPnlUsd,
            stakeUsd == 0m ? 0m : totalPnlUsd * 100m / stakeUsd,
            closedStakeUsd == 0m ? 0m : realizedPnlUsd * 100m / closedStakeUsd,
            Average(state.EntryDelayTotalSeconds, state.EntryDelayCount),
            state.MaxEntryDelaySeconds,
            Average(state.CountertrendScoreSumBps, state.CountertrendScoreCount),
            Average(state.CountertrendSignalSumBps, state.CountertrendSignalCount),
            state.LastCountertrendSignalBps,
            state.LiveOrdersCount,
            state.LiveFilledOrdersCount,
            state.LiveOpenOrdersCount,
            state.LiveSettledOrdersCount,
            liveSkipped,
            runConditionSkips,
            liveTechnical,
            liveIgnored,
            runIgnoredGtd,
            state.LiveIgnoredCancelledCount,
            state.LiveIgnoredRejectedCount,
            state.LiveWonCount,
            state.LiveLostCount,
            state.LiveStakeUsd,
            state.LiveRealizedPnlUsd,
            Percent(state.LiveWonCount, state.LiveSettledOrdersCount),
            Percent(state.LiveLostCount, state.LiveSettledOrdersCount),
            Average(state.LiveWinPnlSumUsd, state.LiveWinCount),
            Average(state.LiveLossPnlSumUsd, state.LiveLossCount),
            state.LiveLossAbsPnlUsd == 0m ? null : state.LivePositivePnlUsd / state.LiveLossAbsPnlUsd,
            Average(state.LiveRealizedPnlUsd, state.LiveSettledOrdersCount),
            state.LiveStakeUsd == 0m ? 0m : state.LiveRealizedPnlUsd * 100m / state.LiveStakeUsd,
            state.LiveLastOrderUtc,
            state.LiveLastSettlementUtc,
            state.LastOrderUtc,
            state.LastRunUtc);
    }

    public static StrategyRecentPerformance ToStrategyRecentPerformance(
        DashboardStrategyDescriptor strategy,
        DashboardRecentProjectionState state,
        int windowHours,
        DateTimeOffset nowUtc)
    {
        var runConditionSkips = strategy.LiveStakes ? state.RunLiveConditionSkippedCount : 0;
        var runTechnicalSkips = strategy.LiveStakes ? state.RunLiveTechnicalSkippedCount : 0;
        var runIgnoredGtd = strategy.LiveStakes ? state.RunLiveIgnoredGtdCount : 0;
        var liveTechnical = runTechnicalSkips + state.LiveTechnicalSkippedCount;
        var liveIgnored = runIgnoredGtd + state.LiveIgnoredCancelledCount + state.LiveIgnoredRejectedCount;
        var topSkip = state.SkipReasonCounts
            .Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}:{pair.Value}")
            .FirstOrDefault() ?? string.Empty;

        return new StrategyRecentPerformance(
            strategy.StrategyId,
            strategy.Code,
            strategy.Name,
            strategy.LiveStakes,
            $"{windowHours}h",
            windowHours,
            nowUtc.AddHours(-windowHours),
            nowUtc,
            state.OrdersCount,
            state.FilledOrdersCount,
            state.ExpiredOrdersCount,
            state.OpenOrdersCount,
            state.EnteredRunsCount,
            state.SkippedRunsCount,
            state.PaperConditionSkippedRunsCount,
            state.PaperNotAcceptedRunsCount,
            state.SettledRunsCount,
            state.WonRunsCount,
            state.LostRunsCount,
            state.FilledCostUsd,
            state.RealizedPnlUsd,
            state.FilledSizeShares == 0m ? 0m : state.FilledCostUsd / state.FilledSizeShares,
            Average(state.EntryDelayTotalSeconds, state.EntryDelayCount),
            state.MaxEntryDelaySeconds,
            Percent(state.WonRunsCount, state.SettledRunsCount),
            state.SettledStakeUsd > 0m
                ? state.RealizedPnlUsd * 100m / state.SettledStakeUsd
                : state.FilledCostUsd > 0m
                    ? state.RealizedPnlUsd * 100m / state.FilledCostUsd
                    : 0m,
            state.LiveSettledOrdersCount,
            runConditionSkips + liveTechnical + liveIgnored,
            runConditionSkips,
            liveTechnical,
            liveIgnored,
            runIgnoredGtd,
            state.LiveIgnoredCancelledCount,
            state.LiveIgnoredRejectedCount,
            state.LiveWonCount,
            state.LiveLostCount,
            state.LiveRealizedPnlUsd,
            state.LiveStakeUsd == 0m ? 0m : state.LiveRealizedPnlUsd * 100m / state.LiveStakeUsd,
            topSkip,
            state.LastOrderUtc,
            state.LastRunUtc);
    }

    private static DashboardRecentProjectionFact CreateFact(
        string sourceKind,
        Guid sourceId,
        string factKind,
        Guid strategyId,
        DateTimeOffset occurredAtUtc,
        DashboardRecentContribution contribution)
    {
        return new DashboardRecentProjectionFact(
            sourceKind,
            sourceId,
            factKind,
            strategyId,
            occurredAtUtc,
            contribution,
            false,
            false,
            false);
    }

    private static DashboardLiveSkipKind ClassifyLiveSkip(StrategyRunProjectionPayload payload)
    {
        if (!IsStatus(payload.Status, StrategyMarketPaperRunStatuses.Skipped) ||
            payload.LiveEnabledAtUtc is null ||
            payload.UpdatedAtUtc < payload.LiveEnabledAtUtc)
        {
            return DashboardLiveSkipKind.None;
        }

        var reason = payload.SkipReason ?? string.Empty;
        if (reason.Equals("gtd_limit_not_filled", StringComparison.OrdinalIgnoreCase))
        {
            return DashboardLiveSkipKind.IgnoredGtd;
        }

        if (ConditionSkipReasons.Contains(reason) ||
            ConditionSkipFragments.Any(fragment => reason.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return DashboardLiveSkipKind.Condition;
        }

        return DashboardLiveSkipKind.Technical;
    }

    private static decimal? GetEntryDelaySeconds(StrategyRunProjectionPayload payload)
    {
        if (payload.EnteredAtUtc is null)
        {
            return null;
        }

        return Math.Max(0m, (decimal)(payload.EnteredAtUtc.Value - payload.EntryDueAtUtc).TotalSeconds);
    }

    private static decimal GetLiveStakeUsd(LiveOrderProjectionPayload payload)
    {
        if (payload.CostBasisUsd > 0m)
        {
            return payload.CostBasisUsd;
        }

        if (payload.FilledNotionalUsd > 0m)
        {
            return payload.FilledNotionalUsd + payload.FeeUsd;
        }

        return payload.FilledSize > 0m
            ? (payload.Price * payload.FilledSize) + payload.FeeUsd
            : 0m;
    }

    private static bool IsFilledPaperOrderStatus(string status)
    {
        return IsStatus(status, nameof(PaperOrderStatus.Filled)) ||
               IsStatus(status, nameof(PaperOrderStatus.PartiallyFilled)) ||
               IsStatus(status, nameof(PaperOrderStatus.PartiallyFilledExpired));
    }

    private static bool IsExpiredPaperOrderStatus(string status)
    {
        return IsStatus(status, nameof(PaperOrderStatus.Expired)) ||
               IsStatus(status, nameof(PaperOrderStatus.PartiallyFilledExpired));
    }

    private static bool IsOpenPaperOrderStatus(string status)
    {
        return IsStatus(status, nameof(PaperOrderStatus.Pending)) ||
               IsStatus(status, nameof(PaperOrderStatus.PartiallyFilled));
    }

    private static bool IsOpenLiveOrderStatus(string status)
    {
        return IsStatus(status, nameof(LiveOrderStatus.Submitted)) ||
               IsStatus(status, nameof(LiveOrderStatus.Live)) ||
               IsStatus(status, nameof(LiveOrderStatus.Delayed)) ||
               IsStatus(status, nameof(LiveOrderStatus.Unmatched)) ||
               IsStatus(status, nameof(LiveOrderStatus.CancelRequested));
    }

    private static bool IsIgnoredCancelledLiveOrder(LiveOrderProjectionPayload payload)
    {
        return payload.FilledSize <= 0m &&
               (IsStatus(payload.Status, nameof(LiveOrderStatus.Cancelled)) ||
                IsStatus(payload.Status, nameof(LiveOrderStatus.CancelFailed)));
    }

    private static bool IsIgnoredRejectedLiveOrder(string status)
    {
        return IsStatus(status, nameof(LiveOrderStatus.Rejected)) ||
               IsStatus(status, nameof(LiveOrderStatus.Error));
    }

    private static bool IsBuy(string side) => side.Equals("Buy", StringComparison.OrdinalIgnoreCase);

    private static bool IsSell(string side) => side.Equals("Sell", StringComparison.OrdinalIgnoreCase);

    private static bool IsStatus(string actual, string expected) =>
        actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static decimal Average(decimal sum, int count) => count == 0 ? 0m : sum / count;

    private static decimal Percent(int numerator, int denominator) =>
        denominator == 0 ? 0m : numerator * 100m / denominator;

    private static DateTimeOffset? Latest(DateTimeOffset? current, DateTimeOffset? candidate)
    {
        return candidate is not null && (current is null || candidate > current)
            ? candidate
            : current;
    }

    private static bool IsRemovedMaximum(decimal oldValue, decimal? newValue, decimal currentValue) =>
        oldValue > 0m && oldValue == currentValue && (newValue ?? 0m) < oldValue;

    private static bool IsRemovedLatest(
        DateTimeOffset? oldValue,
        DateTimeOffset? newValue,
        DateTimeOffset? currentValue) =>
        oldValue is not null && oldValue == currentValue && (newValue is null || newValue < oldValue);

    private static void ValidateNonnegativeCounts(DashboardLifetimeProjectionState state)
    {
        var counts = new[]
        {
            state.OrdersCount,
            state.FilledOrdersCount,
            state.OpenOrdersCount,
            state.CountertrendScoreCount,
            state.CountertrendSignalCount,
            state.OpenPositionsCount,
            state.SettlementCount,
            state.SettlementWonCount,
            state.SettlementLostCount,
            state.SettlementWinCount,
            state.SettlementLossCount,
            state.RunsCount,
            state.ObservedRunsCount,
            state.EnteredRunsCount,
            state.SkippedRunsCount,
            state.PaperConditionSkippedRunsCount,
            state.PaperNotAcceptedRunsCount,
            state.SettledRunsCount,
            state.RunWonCount,
            state.RunLostCount,
            state.RunWinCount,
            state.RunLossCount,
            state.EntryDelayCount,
            state.RunLiveConditionSkippedCount,
            state.RunLiveTechnicalSkippedCount,
            state.RunLiveIgnoredGtdCount,
            state.LiveOrdersCount,
            state.LiveFilledOrdersCount,
            state.LiveOpenOrdersCount,
            state.LiveSettledOrdersCount,
            state.LiveTechnicalSkippedCount,
            state.LiveIgnoredCancelledCount,
            state.LiveIgnoredRejectedCount,
            state.LiveWonCount,
            state.LiveLostCount,
            state.LiveWinCount,
            state.LiveLossCount
        };
        if (counts.Any(value => value < 0))
        {
            throw new InvalidOperationException("Dashboard lifetime projection count became negative.");
        }
    }

    private static void ValidateNonnegativeCounts(DashboardRecentProjectionState state)
    {
        var counts = new[]
        {
            state.OrdersCount,
            state.FilledOrdersCount,
            state.ExpiredOrdersCount,
            state.OpenOrdersCount,
            state.EnteredRunsCount,
            state.SkippedRunsCount,
            state.PaperConditionSkippedRunsCount,
            state.PaperNotAcceptedRunsCount,
            state.RunLiveConditionSkippedCount,
            state.RunLiveTechnicalSkippedCount,
            state.RunLiveIgnoredGtdCount,
            state.SettledRunsCount,
            state.WonRunsCount,
            state.LostRunsCount,
            state.EntryDelayCount,
            state.LiveSettledOrdersCount,
            state.LiveTechnicalSkippedCount,
            state.LiveIgnoredCancelledCount,
            state.LiveIgnoredRejectedCount,
            state.LiveWonCount,
            state.LiveLostCount
        };
        if (counts.Any(value => value < 0))
        {
            throw new InvalidOperationException("Dashboard recent projection count became negative.");
        }
    }

    private enum DashboardLiveSkipKind
    {
        None,
        Condition,
        Technical,
        IgnoredGtd
    }
}

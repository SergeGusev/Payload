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
        var isSell = IsSell(payload.OrderSide);
        var feeAccounted = isSell && IsSellFeeAccounted(
            payload.FeeAccountingStatus,
            payload.FeeUsd,
            payload.RealizedPnlUsd,
            payload.NetRealizedPnlUsd);

        return new DashboardLifetimeContribution
        {
            FillRealizedPnlUsd = payload.RealizedPnlUsd,
            FillClosedCostBasisUsd = isSell
                ? (payload.Price * payload.SizeShares) - payload.RealizedPnlUsd
                : 0m,
            FillNetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
            // A SELL fill's stored fee is the exit fee only. Gross minus net also
            // includes the allocated entry fee and is therefore the complete fee.
            FillAccountedFeeUsd = feeAccounted
                ? payload.RealizedPnlUsd - payload.NetRealizedPnlUsd!.Value
                : 0m,
            FillFeeAccountedSettledCount = feeAccounted ? 1 : 0,
            FillFeeRequiredSettledCount = isSell ? 1 : 0
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
        var feeAccounted = isSettled && IsFeeAccounted(
            payload.FeeAccountingStatus,
            payload.FeeUsd,
            payload.RealizedPnlUsd,
            payload.NetRealizedPnlUsd);
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
            RunNetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
            RunAccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
            RunFeeAccountedSettledCount = feeAccounted ? 1 : 0,
            RunFeeRequiredSettledCount = isSettled ? 1 : 0,
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

    public static DashboardLifetimeContribution GetLifetimeContribution(
        StrategyPaperSkipRollupProjectionPayload payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(payload.RunCount);

        return new DashboardLifetimeContribution
        {
            RunsCount = payload.RunCount,
            SkippedRunsCount = payload.RunCount,
            PaperConditionSkippedRunsCount = payload.RunCount,
            LastRunUtc = payload.LastRunUtc
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperPositionProjectionPayload payload)
    {
        var isOpen = payload.SizeShares > 0m;
        var feeAccounted = isOpen && IsFeeAccounted(
            payload.FeeAccountingStatus,
            payload.FeeUsd,
            payload.UnrealizedPnlUsd,
            payload.NetUnrealizedPnlUsd);

        return new DashboardLifetimeContribution
        {
            OpenPositionsCount = isOpen ? 1 : 0,
            UnrealizedPnlUsd = isOpen ? payload.UnrealizedPnlUsd : 0m,
            OpenPositionCostBasisUsd = isOpen ? payload.AveragePrice * payload.SizeShares : 0m,
            NetUnrealizedPnlUsd = feeAccounted ? payload.NetUnrealizedPnlUsd!.Value : 0m,
            OpenPositionAccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
            FeeAccountedOpenPositionCount = feeAccounted ? 1 : 0,
            FeeRequiredOpenPositionCount = isOpen ? 1 : 0
        };
    }

    public static DashboardLifetimeContribution GetLifetimeContribution(PaperSettlementProjectionPayload payload)
    {
        var feeAccounted = IsFeeAccounted(
            payload.FeeAccountingStatus,
            payload.FeeUsd,
            payload.RealizedPnlUsd,
            payload.NetRealizedPnlUsd);

        return new DashboardLifetimeContribution
        {
            SettlementCount = 1,
            SettlementWonCount = payload.Won ? 1 : 0,
            SettlementLostCount = payload.Won ? 0 : 1,
            SettlementCostBasisUsd = payload.CostBasisUsd,
            SettlementRealizedPnlUsd = payload.RealizedPnlUsd,
            SettlementNetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
            SettlementAccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
            SettlementFeeAccountedSettledCount = feeAccounted ? 1 : 0,
            SettlementFeeRequiredSettledCount = 1,
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
        var feeAccounted = countedAsSettled && IsFeeAccounted(
            payload.FeeAccountingStatus,
            payload.FeeUsd,
            payload.RealizedPnlUsd,
            payload.NetRealizedPnlUsd);

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
            LiveNetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
            LiveAccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
            LiveFeeAccountedSettledCount = feeAccounted ? 1 : 0,
            LiveFeeRequiredSettledCount = countedAsSettled ? 1 : 0,
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
            var feeAccounted = IsFeeAccounted(
                payload.FeeAccountingStatus,
                payload.FeeUsd,
                payload.RealizedPnlUsd,
                payload.NetRealizedPnlUsd);
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
                    RealizedPnlUsd = realizedPnl,
                    NetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
                    AccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
                    FeeAccountedSettledCount = feeAccounted ? 1 : 0,
                    FeeRequiredSettledCount = 1
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
            var countedAsSettled = payload.RealizedPnlUsd is not null;
            var feeAccounted = countedAsSettled && IsFeeAccounted(
                payload.FeeAccountingStatus,
                payload.FeeUsd,
                payload.RealizedPnlUsd,
                payload.NetRealizedPnlUsd);
            facts.Add(CreateFact(
                DashboardProjectionSourceKinds.LiveOrder,
                payload.Id,
                DashboardProjectionFactKinds.LiveOrderSettled,
                payload.StrategyId,
                payload.SettledAtUtc.Value,
                new DashboardRecentContribution
                {
                    LiveSettledOrdersCount = countedAsSettled ? 1 : 0,
                    LiveWonCount = won ? 1 : 0,
                    LiveLostCount = won ? 0 : 1,
                    LiveStakeUsd = GetLiveStakeUsd(payload),
                    LiveRealizedPnlUsd = payload.RealizedPnlUsd ?? 0m,
                    LiveNetRealizedPnlUsd = feeAccounted ? payload.NetRealizedPnlUsd!.Value : 0m,
                    LiveAccountedFeeUsd = feeAccounted ? payload.FeeUsd : 0m,
                    LiveFeeAccountedSettledCount = feeAccounted ? 1 : 0,
                    LiveFeeRequiredSettledCount = countedAsSettled ? 1 : 0
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
        state.FillNetRealizedPnlUsd += sign * contribution.FillNetRealizedPnlUsd;
        state.FillAccountedFeeUsd += sign * contribution.FillAccountedFeeUsd;
        state.FillFeeAccountedSettledCount += sign * contribution.FillFeeAccountedSettledCount;
        state.FillFeeRequiredSettledCount += sign * contribution.FillFeeRequiredSettledCount;
        state.OpenPositionsCount += sign * contribution.OpenPositionsCount;
        state.UnrealizedPnlUsd += sign * contribution.UnrealizedPnlUsd;
        state.OpenPositionCostBasisUsd += sign * contribution.OpenPositionCostBasisUsd;
        state.NetUnrealizedPnlUsd += sign * contribution.NetUnrealizedPnlUsd;
        state.OpenPositionAccountedFeeUsd += sign * contribution.OpenPositionAccountedFeeUsd;
        state.FeeAccountedOpenPositionCount += sign * contribution.FeeAccountedOpenPositionCount;
        state.FeeRequiredOpenPositionCount += sign * contribution.FeeRequiredOpenPositionCount;
        state.SettlementCount += sign * contribution.SettlementCount;
        state.SettlementWonCount += sign * contribution.SettlementWonCount;
        state.SettlementLostCount += sign * contribution.SettlementLostCount;
        state.SettlementCostBasisUsd += sign * contribution.SettlementCostBasisUsd;
        state.SettlementRealizedPnlUsd += sign * contribution.SettlementRealizedPnlUsd;
        state.SettlementNetRealizedPnlUsd += sign * contribution.SettlementNetRealizedPnlUsd;
        state.SettlementAccountedFeeUsd += sign * contribution.SettlementAccountedFeeUsd;
        state.SettlementFeeAccountedSettledCount += sign * contribution.SettlementFeeAccountedSettledCount;
        state.SettlementFeeRequiredSettledCount += sign * contribution.SettlementFeeRequiredSettledCount;
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
        state.RunNetRealizedPnlUsd += sign * contribution.RunNetRealizedPnlUsd;
        state.RunAccountedFeeUsd += sign * contribution.RunAccountedFeeUsd;
        state.RunFeeAccountedSettledCount += sign * contribution.RunFeeAccountedSettledCount;
        state.RunFeeRequiredSettledCount += sign * contribution.RunFeeRequiredSettledCount;
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
        state.LiveNetRealizedPnlUsd += sign * contribution.LiveNetRealizedPnlUsd;
        state.LiveAccountedFeeUsd += sign * contribution.LiveAccountedFeeUsd;
        state.LiveFeeAccountedSettledCount += sign * contribution.LiveFeeAccountedSettledCount;
        state.LiveFeeRequiredSettledCount += sign * contribution.LiveFeeRequiredSettledCount;
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
        state.NetRealizedPnlUsd += sign * contribution.NetRealizedPnlUsd;
        state.AccountedFeeUsd += sign * contribution.AccountedFeeUsd;
        state.FeeAccountedSettledCount += sign * contribution.FeeAccountedSettledCount;
        state.FeeRequiredSettledCount += sign * contribution.FeeRequiredSettledCount;
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
        state.LiveNetRealizedPnlUsd += sign * contribution.LiveNetRealizedPnlUsd;
        state.LiveAccountedFeeUsd += sign * contribution.LiveAccountedFeeUsd;
        state.LiveFeeAccountedSettledCount += sign * contribution.LiveFeeAccountedSettledCount;
        state.LiveFeeRequiredSettledCount += sign * contribution.LiveFeeRequiredSettledCount;

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
        var feeAccountedSettledCount = usesRuns
            ? state.RunFeeAccountedSettledCount
            : state.SettlementFeeAccountedSettledCount + state.FillFeeAccountedSettledCount;
        var feeRequiredSettledCount = usesRuns
            ? state.RunFeeRequiredSettledCount
            : state.SettlementFeeRequiredSettledCount + state.FillFeeRequiredSettledCount;
        var closedAccountedFeeUsd = usesRuns
            ? state.RunAccountedFeeUsd
            : state.SettlementAccountedFeeUsd + state.FillAccountedFeeUsd;
        var accountedNetRealizedPnlUsd = usesRuns
            ? state.RunNetRealizedPnlUsd
            : state.SettlementNetRealizedPnlUsd + state.FillNetRealizedPnlUsd;
        var closedFeesComplete = feeAccountedSettledCount == feeRequiredSettledCount;
        var openFeesComplete = state.FeeAccountedOpenPositionCount == state.FeeRequiredOpenPositionCount;
        decimal? netRealizedPnlUsd = closedFeesComplete ? accountedNetRealizedPnlUsd : null;
        decimal? netUnrealizedPnlUsd = openFeesComplete ? state.NetUnrealizedPnlUsd : null;
        decimal? netTotalPnlUsd = netRealizedPnlUsd is not null && netUnrealizedPnlUsd is not null
            ? netRealizedPnlUsd.Value + netUnrealizedPnlUsd.Value
            : null;
        var netClosedDenominatorUsd = closedStakeUsd + closedAccountedFeeUsd;
        var netOpenDenominatorUsd = state.OpenPositionCostBasisUsd + state.OpenPositionAccountedFeeUsd;
        var netTotalDenominatorUsd = netClosedDenominatorUsd + netOpenDenominatorUsd;
        decimal? netClosedRoiPct = netRealizedPnlUsd is null
            ? null
            : netClosedDenominatorUsd == 0m
                ? 0m
                : netRealizedPnlUsd.Value * 100m / netClosedDenominatorUsd;
        decimal? netRoiPct = netTotalPnlUsd is null
            ? null
            : netTotalDenominatorUsd == 0m
                ? 0m
                : netTotalPnlUsd.Value * 100m / netTotalDenominatorUsd;
        var liveFeesComplete = state.LiveFeeAccountedSettledCount == state.LiveFeeRequiredSettledCount;
        decimal? liveNetRealizedPnlUsd = liveFeesComplete ? state.LiveNetRealizedPnlUsd : null;
        var liveNetDenominatorUsd = state.LiveStakeUsd + state.LiveAccountedFeeUsd;
        decimal? liveNetRoiPct = liveNetRealizedPnlUsd is null
            ? null
            : liveNetDenominatorUsd == 0m
                ? 0m
                : liveNetRealizedPnlUsd.Value * 100m / liveNetDenominatorUsd;
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
            state.LastRunUtc,
            netRealizedPnlUsd,
            netUnrealizedPnlUsd,
            netTotalPnlUsd,
            netRoiPct,
            netClosedRoiPct,
            closedAccountedFeeUsd + state.OpenPositionAccountedFeeUsd,
            feeAccountedSettledCount,
            feeRequiredSettledCount,
            state.FeeAccountedOpenPositionCount,
            state.FeeRequiredOpenPositionCount,
            liveNetRealizedPnlUsd,
            liveNetRoiPct,
            state.LiveAccountedFeeUsd,
            state.LiveFeeAccountedSettledCount,
            state.LiveFeeRequiredSettledCount);
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
        var feesComplete = state.FeeAccountedSettledCount == state.FeeRequiredSettledCount;
        decimal? netRealizedPnlUsd = feesComplete ? state.NetRealizedPnlUsd : null;
        var netDenominatorUsd = state.SettledStakeUsd + state.AccountedFeeUsd;
        decimal? netRoiPct = netRealizedPnlUsd is null
            ? null
            : netDenominatorUsd == 0m
                ? 0m
                : netRealizedPnlUsd.Value * 100m / netDenominatorUsd;
        var liveFeesComplete = state.LiveFeeAccountedSettledCount == state.LiveFeeRequiredSettledCount;
        decimal? liveNetRealizedPnlUsd = liveFeesComplete ? state.LiveNetRealizedPnlUsd : null;
        var liveNetDenominatorUsd = state.LiveStakeUsd + state.LiveAccountedFeeUsd;
        decimal? liveNetRoiPct = liveNetRealizedPnlUsd is null
            ? null
            : liveNetDenominatorUsd == 0m
                ? 0m
                : liveNetRealizedPnlUsd.Value * 100m / liveNetDenominatorUsd;

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
            state.LastRunUtc,
            netRealizedPnlUsd,
            netRoiPct,
            state.AccountedFeeUsd,
            state.FeeAccountedSettledCount,
            state.FeeRequiredSettledCount,
            liveNetRealizedPnlUsd,
            liveNetRoiPct,
            state.LiveAccountedFeeUsd,
            state.LiveFeeAccountedSettledCount,
            state.LiveFeeRequiredSettledCount);
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
        if (payload.FilledNotionalUsd > 0m)
        {
            return payload.FilledNotionalUsd;
        }

        if (payload.FilledSize > 0m)
        {
            return payload.Price * payload.FilledSize;
        }

        return payload.CostBasisUsd > 0m
            ? Math.Max(0m, payload.CostBasisUsd - payload.FeeUsd)
            : 0m;
    }

    private static bool IsFeeAccounted(
        string? feeAccountingStatus,
        decimal feeUsd,
        decimal? grossPnlUsd,
        decimal? netPnlUsd)
    {
        return FeeAccountingRules.IsAccounted(feeAccountingStatus) &&
               feeUsd >= 0m &&
               grossPnlUsd is not null &&
               netPnlUsd is not null &&
               netPnlUsd.Value == grossPnlUsd.Value - feeUsd;
    }

    private static bool IsSellFeeAccounted(
        string? feeAccountingStatus,
        decimal exitFeeUsd,
        decimal grossPnlUsd,
        decimal? netPnlUsd)
    {
        return FeeAccountingRules.IsAccounted(feeAccountingStatus) &&
               exitFeeUsd >= 0m &&
               netPnlUsd is not null &&
               grossPnlUsd - netPnlUsd.Value >= exitFeeUsd;
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
            state.FillFeeAccountedSettledCount,
            state.FillFeeRequiredSettledCount,
            state.OpenPositionsCount,
            state.FeeAccountedOpenPositionCount,
            state.FeeRequiredOpenPositionCount,
            state.SettlementCount,
            state.SettlementWonCount,
            state.SettlementLostCount,
            state.SettlementWinCount,
            state.SettlementLossCount,
            state.SettlementFeeAccountedSettledCount,
            state.SettlementFeeRequiredSettledCount,
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
            state.RunFeeAccountedSettledCount,
            state.RunFeeRequiredSettledCount,
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
            state.LiveLossCount,
            state.LiveFeeAccountedSettledCount,
            state.LiveFeeRequiredSettledCount
        };
        if (counts.Any(value => value < 0))
        {
            throw new InvalidOperationException("Dashboard lifetime projection count became negative.");
        }

        if (state.FillFeeAccountedSettledCount > state.FillFeeRequiredSettledCount ||
            state.SettlementFeeAccountedSettledCount > state.SettlementFeeRequiredSettledCount ||
            state.RunFeeAccountedSettledCount > state.RunFeeRequiredSettledCount ||
            state.FeeAccountedOpenPositionCount > state.FeeRequiredOpenPositionCount ||
            state.LiveFeeAccountedSettledCount > state.LiveFeeRequiredSettledCount)
        {
            throw new InvalidOperationException(
                "Dashboard lifetime fee-accounted count exceeded its required count.");
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
            state.FeeAccountedSettledCount,
            state.FeeRequiredSettledCount,
            state.EntryDelayCount,
            state.LiveSettledOrdersCount,
            state.LiveTechnicalSkippedCount,
            state.LiveIgnoredCancelledCount,
            state.LiveIgnoredRejectedCount,
            state.LiveWonCount,
            state.LiveLostCount,
            state.LiveFeeAccountedSettledCount,
            state.LiveFeeRequiredSettledCount
        };
        if (counts.Any(value => value < 0))
        {
            throw new InvalidOperationException("Dashboard recent projection count became negative.");
        }

        if (state.FeeAccountedSettledCount > state.FeeRequiredSettledCount ||
            state.LiveFeeAccountedSettledCount > state.LiveFeeRequiredSettledCount)
        {
            throw new InvalidOperationException(
                "Dashboard recent fee-accounted count exceeded its required count.");
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

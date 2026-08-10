using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

internal static class MakerGtdPaperExecutionContract
{
    public const string ExecutionSource = MakerGtdPaperExecutionSources.ReferenceAverage;
    public const string LegacyContractVersion = "maker_gtd_paper_v1";
    public const string CurrentContractVersion = "maker_gtd_paper_v2";
    public const string LegacyPriceFormula =
        "floor_to_tick(min(best_bid + tick_size, best_ask - tick_size, maximum_order_price))";
    public const string CurrentPriceFormula =
        "floor_to_tick(min(best_ask - tick_size, maximum_order_price))";
    public const string ExpiredUnfilledReasonCode = "maker_gtd_expired_unfilled";
    public const string EvidenceUnavailableReasonCode = "maker_gtd_evidence_unavailable";
    public const string MarketDataParseFailureCode = "maker_gtd_market_data_parse_failed";
    public const string MarketDataDispatchFailureCode = "maker_gtd_market_data_dispatch_failed";
    public const string MarketDataEnqueueFailureCode = "maker_gtd_market_data_enqueue_failed";
    public const string MarketDataHandlerFailureCode = "maker_gtd_market_data_handler_failed";
    public const string MarketDataApplyFailureCode = "maker_gtd_market_data_apply_failed";
    public const string MarketDataFailureHistoryIncompleteCode = "maker_gtd_market_data_failure_history_incomplete";

    public static bool IsMakerGtdOrder(PaperOrder order)
    {
        return MakerGtdPaperExecutionSources.IsSupported(order.ExecutionSource);
    }

    public static bool IsApprovedCurrentStrategyVariant(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.Behavior != BtcUpDown5mStrategyBehavior.ReferenceAverageBpsThresholdMakerGtdPremarket ||
            !variant.PaperOnly ||
            variant.Direction != BtcUpDown5mStrategyDirection.Dynamic ||
            variant.EntryDelaySeconds != -30 ||
            variant.MarketInterval != BtcUpDownMarketInterval.FiveMinutes ||
            !string.Equals(variant.ReferenceAssetSymbol, "ETH", StringComparison.Ordinal) ||
            variant.FixedOutcome is not null ||
            variant.DiffCounterTriggerOutcome is not null ||
            variant.MakerMaximumOrderPrice != StrategyIds.ReferenceAverageMakerGtdMaximumOrderPrice ||
            variant.DecisionThresholdBps is not { } thresholdBps ||
            decimal.Truncate(thresholdBps) != thresholdBps)
        {
            return false;
        }

        var threshold = decimal.ToInt32(thresholdBps);
        if (threshold is not (>= 1 and <= 10) &&
            (threshold < 15 || threshold > 100 || threshold % 5 != 0))
        {
            return false;
        }

        return variant.Id == Guid.Parse(
                   $"b7c50005-0000-4000-8223-{100 + threshold:000000000000}") &&
            string.Equals(
                variant.Code,
                $"eth_up_down_5m_reference_average_bps_{threshold}_maker_gtd_premarket",
                StringComparison.Ordinal);
    }
}

internal static class PairedMakerGtdPaperExecutionContract
{
    public const string ExecutionSource = MakerGtdPaperExecutionSources.PairedFirstAccepting;
    public const string LegacyContractVersion = "paired_maker_gtd_paper_v1";
    public const string DirectHttpReceiptContractVersion = "paired_maker_gtd_paper_v2";
    public const string GapRecoveryContractVersion = "paired_maker_gtd_paper_v3";
    public const string CurrentContractVersion = "paired_maker_gtd_paper_v4";
    public const string ContractVersion = CurrentContractVersion;
    public const string DirectHttpReceiptFreshnessBasis = "direct_http_response_receipt";
    public const string GapRecoveryLifecyclePolicyVersion =
        "paired_touch_no_depth_gap_recovery_v1";
    public const string GapRecoveryAuditQualifier =
        "observation gaps are not backfilled; only future authoritative events after confirmed recovery are eligible";
    public const long MaximumDirectHttpQuoteAgeMilliseconds = 60_000;
    public const string PriceFormula =
        "floor_to_tick(min(best_ask - tick_size, maximum_order_price))";
    public const string MandatoryLabel = StrategyIds.OptimisticTouchNoDepthPaperLabel;

    public static bool IsSupportedContractVersion(string? contractVersion)
    {
        return string.Equals(contractVersion, LegacyContractVersion, StringComparison.Ordinal) ||
            string.Equals(contractVersion, DirectHttpReceiptContractVersion, StringComparison.Ordinal) ||
            string.Equals(contractVersion, GapRecoveryContractVersion, StringComparison.Ordinal) ||
            string.Equals(contractVersion, CurrentContractVersion, StringComparison.Ordinal);
    }

    public static bool UsesGapRecoveryLifecycle(string? contractVersion)
    {
        return string.Equals(contractVersion, GapRecoveryContractVersion, StringComparison.Ordinal) ||
            string.Equals(contractVersion, CurrentContractVersion, StringComparison.Ordinal);
    }

    public static bool UsesMarketEndEffectiveExpiry(string? contractVersion)
    {
        return string.Equals(contractVersion, CurrentContractVersion, StringComparison.Ordinal);
    }

    public static bool IsApprovedStrategyVariant(BtcUpDown5mStrategyVariant variant)
    {
        if (variant.Behavior != BtcUpDown5mStrategyBehavior.PairedFixedOutcomeMakerGtdFirstAccepting ||
            !variant.PaperOnly ||
            variant.Direction != BtcUpDown5mStrategyDirection.Dynamic ||
            variant.EntryTiming != BtcUpDown5mStrategyEntryTiming.FirstAcceptingOrders ||
            variant.EntryDelaySeconds != StrategyIds.PairedMakerGtdNominalEntryDelaySeconds ||
            variant.MarketInterval != BtcUpDownMarketInterval.FiveMinutes ||
            variant.FixedOutcome is not { } outcome ||
            variant.PairedStrategyId is not { } pairedStrategyId ||
            variant.MakerMaximumOrderPrice is not { } maximumOrderPrice)
        {
            return false;
        }

        var assetOffset = variant.ReferenceAssetSymbol switch
        {
            "BTC" => 100,
            "ETH" => 200,
            "SOL" => 300,
            _ => 0
        };
        if (assetOffset == 0)
        {
            return false;
        }

        var outcomeOffset = outcome == BtcUpDownFixedOutcome.Up ? 1 : 2;
        var pairedOutcomeOffset = outcome == BtcUpDownFixedOutcome.Up ? 2 : 1;
        var expectedCap = outcome == BtcUpDownFixedOutcome.Up
            ? StrategyIds.PairedMakerGtdUpMaximumOrderPrice
            : StrategyIds.PairedMakerGtdDownMaximumOrderPrice;
        var assetCode = variant.ReferenceAssetSymbol.ToLowerInvariant();
        var outcomeCode = outcome.ToString().ToLowerInvariant();

        return maximumOrderPrice == expectedCap &&
            variant.Id == Guid.Parse(
                $"b7c50005-0000-4000-8224-{assetOffset + outcomeOffset:000000000000}") &&
            pairedStrategyId == Guid.Parse(
                $"b7c50005-0000-4000-8224-{assetOffset + pairedOutcomeOffset:000000000000}") &&
            string.Equals(
                variant.Code,
                $"{assetCode}_up_down_5m_{outcomeCode}_paired_maker_gtd_first_accepting",
                StringComparison.Ordinal);
    }
}

using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

internal static class MakerGtdPaperExecutionContract
{
    public const string ExecutionSource = "eth_reference_average_maker_gtd_paper";
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
        return string.Equals(
            order.ExecutionSource,
            ExecutionSource,
            StringComparison.Ordinal);
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

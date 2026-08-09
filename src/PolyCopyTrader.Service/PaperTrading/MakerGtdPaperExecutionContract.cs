using PolyCopyTrader.Domain;

namespace PolyCopyTrader.Service.PaperTrading;

internal static class MakerGtdPaperExecutionContract
{
    public const string ExecutionSource = "eth_reference_average_maker_gtd_paper";
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
}

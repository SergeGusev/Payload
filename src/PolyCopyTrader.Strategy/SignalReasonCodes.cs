namespace PolyCopyTrader.Strategy;

public static class SignalReasonCodes
{
    public const string TraderDisabled = "trader_disabled";
    public const string CategoryNotAllowed = "category_not_allowed";
    public const string UnsupportedSide = "unsupported_side";
    public const string TradeTooOld = "trade_too_old";
    public const string LeaderTradeTooSmall = "leader_trade_too_small";
    public const string MissingOrderBook = "missing_orderbook";
    public const string MissingMarketMetadata = "missing_market_metadata";
    public const string SpreadTooWideAbs = "spread_too_wide_abs";
    public const string SpreadTooWidePct = "spread_too_wide_pct";
    public const string PriceMovedTooFar = "price_moved_too_far";
    public const string NoSafeMakerPrice = "no_safe_maker_price";
    public const string RiskTradeLimit = "risk_trade_limit";
    public const string RiskMarketLimit = "risk_market_limit";
    public const string RiskTraderLimit = "risk_trader_limit";
    public const string RiskCategoryLimit = "risk_category_limit";
    public const string RiskTotalDeployedLimit = "risk_total_deployed_limit";
    public const string RiskDailyLossLimit = "risk_daily_loss_limit";
    public const string RiskOpenOrdersLimit = "risk_open_orders_limit";
    public const string RiskOrderAgeLimit = "risk_order_age_limit";
    public const string MarketTooCloseToEvent = "market_too_close_to_event";
    public const string MarketInactive = "market_inactive";
    public const string MarketResolved = "market_resolved";
    public const string MissingMarketCategory = "missing_market_category";
    public const string MissingLeaderCategoryPerformance = "missing_leader_category_performance";
    public const string LeaderCategoryPerformanceStale = "leader_category_performance_stale";
    public const string LeaderCategoryResolvedSampleTooSmall = "leader_category_resolved_sample_too_small";
    public const string LeaderCategorySampleQualityTooLow = "leader_category_sample_quality_too_low";
    public const string LeaderCategoryScoreTooLow = "leader_category_score_too_low";
    public const string LeaderCategoryRoiTooLow = "leader_category_roi_too_low";
    public const string LeaderCategoryWinRateTooLow = "leader_category_win_rate_too_low";
    public const string ScoreBelowThreshold = "score_below_threshold";
    public const string ObserveOnly = "observe_only";
}

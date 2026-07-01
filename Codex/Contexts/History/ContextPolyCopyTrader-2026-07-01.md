## 2026-06-30T21:20:35.8082438+00:00
Request:
Поле Notional в Paper ставках означает, какая фактическая сумма была поставлена, правильно?

Result:
Reviewed Paper order creation, Dashboard binding, and fill accounting. Clarified that Paper `Notional` is the created order notional, usually `price * sizeShares` or the signal's proposed notional, and is not always the actual filled amount. Actual Paper fill cost is derived from `paper_fills` as `fill.Price * fill.SizeShares`; it matches `Notional` only when the full order fills at the order price. No production DB writes, live orders, service restart, cancel action, source-code behavior changes, build, or tests were performed.

## 2026-07-01T05:35:36.3024000+00:00
Request:
Assess `ETH Up or Down 5m 1 Diff Shift Progress Premarket`: historical drawdown, maximum stake, and required Live balance to enable another strategy in Live.

Result:
Confirmed from source and schema that the requested strategy should be `eth_up_down_5m_1_diff_shift_progress_premarket` with id `b7c50005-0000-4000-8167-000000000001`. Queried production read-only and found no rows in `strategies`, `strategy_market_paper_runs`, `paper_orders`, `paper_fills`, `live_orders`, or raw diagnostics for the target strategy or the broader `816x` Diff Shift Progress group. Therefore there is no production Paper/Live history from which to calculate realized drawdown, maximum historical stake, or win/loss behavior. Source inspection confirmed Paper entries for this premarket variant use `Paper unit * abs(Diff)`, while current paper-live-shadow Live sizing uses the configured `LiveStakeAmount`; Live preflight requires `live_available_balance - open_live_order_notional >= required live notional`, with open statuses `Submitted`, `Live`, `Delayed`, `Unmatched`, and `CancelRequested`. No database writes, live orders, service restart, source-code changes, build, or tests were performed.

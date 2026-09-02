## 2026-09-02T05:23:55.012400Z
Request:
Проверь сервер, ставки, логи и канселы живых ставок

Result:
Read-only Product verification found `PolyCopyTrader.Service` Running/Live on exact build `99cdcf080cece10b74648377f8e6db114c0c3723`, with a 29.7-second heartbeat, NULL `last_error`, connected/non-stale Polymarket market data, and 316/316 recent Paper orders Filled. The expiry correction is now proven by two real post-deploy Live orders: all three linked deadlines matched, both reached Matched and final settlement, Paper/Live accounting agreed, and no post-fix Live cancellation or discrepancy was found. The only two 24-hour cancellations predated the deploy and carried the old immediate-expiry defect. Three later Live attempts were rejected with `HTTP 503: trading is disabled`, so current Live POST availability remains unknown. Logs and Product events also exposed 89 non-fatal settlement-sync reconciliation errors plus transient reference-price/OKX timeouts, one recovered settlement deadlock, and a recovered market-resolution queue burst; service and Paper processing continued. No Product state was changed.

## 2026-09-02T05:27:00.397602Z
Request:
Почему для Live ставок Dashboard всегда показывает Price 0.99 ? Я понимаю, что мы выставляем FAK ставку по этой цене. Но как узнать, по какой цене именно купили?

Result:
Read-only code tracing and Product verification established that Dashboard `Price` is the FAK hard-limit price, while the adjacent `Avg fill` column is the actual execution VWAP. Product contained 3,269 filled non-parity-owned Live rows, all with `average_fill_price`; 2,687 differed from the `0.99` limit. Recent examples showed actual averages `0.80`, `0.68`, `0.61`, and `0.49`, independently confirmed as `filled_notional_usd / filled_size`. No Product or trading state was changed.

## 2026-09-02T05:33:03.3106687+00:00
Request:
Ок, исправь, что нужно

Result:
Read-only diagnosis proved the repeated settlement-sync errors are caused by `SyncPaperShadowAsync` invoking fill reconciliation again after the linked Paper order is already durably marked `paper_live_shadow_actual_fill`; the repository then correctly refuses to mutate the already-settled Paper position. Drafted and mechanically validated the narrow idempotency contract `RC-20260902-paper-live-shadow-settlement-idempotency`, digest `sha256:b02dbb3d0aa848cbe36b0cc2686df59ca0ec7862be1bd99b2145287bd1a3bba7`, preserving shape validation, initial reconciliation, decision links, Live settlement, and accounting. The three explicit CLOB `HTTP 503: trading is disabled` responses have no established local request defect and are excluded from retry/bypass changes. Product code remains unchanged pending the required exact user approval.

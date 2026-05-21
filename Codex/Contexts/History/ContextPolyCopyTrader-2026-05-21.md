## 2026-05-20T21:19:38.4496811+00:00
Request:
Выложил, проверяй

Result:
Verified production after deploy read-only through remote PostgreSQL host `192.168.0.101`. `PolyCopyTrader.Service` is `Running` in `Live` mode on build `info=1.0.0+e547fcd535f2ecbbeafd7259635eda26e5503d0b`, restarted at `2026-05-20T21:13:13Z`, and has fresh heartbeat with empty `last_error`. The new Maker strategy rows exist and are enabled with `live_stakes=false`; Maker runs/orders are `0` as expected because production is in `Live` mode and the strategies are Paper-only. Normal BTC Paper processing continued after restart, Live orders since restart were `0`, Paper/Live-shadow discrepancies for 24h were `0`, market WebSocket recovered to `Connected`, and no API errors appeared after `2026-05-20T21:15:10Z`. No DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-20T21:35:39.7360339+00:00
Request:
Ты меня не понял. Maker-стратегии должны уже работать. Просто не ставить живых ставок, а ставить только Paper ставки для теста

Result:
Corrected BTC 5m Maker behavior so the Maker variants run whenever Paper runtime is enabled, including `Bot:Mode=Live` with `PaperTrading:RunInLiveMode=true`, while still creating only Paper orders. Removed the extra `BotMode.Paper` gate, kept the order path on `CreatePendingOpeningLimitPaperOrder` with `execution_source=btc_updown5m_maker_post_only`, added a Live-mode paper-only regression that verifies no live trading client call and no `live_orders` row, and updated README/configuration docs. Verification passed: targeted BTC/storage tests `152/152`, full tests `512/512`, and `git diff --check` with LF/CRLF warnings only. No DB writes, service restart, live order submission, or cancel action was performed.

## 2026-05-21T03:26:49.2170645Z
Request:
Почему BTC Up or Down 5m Up Maker и BTC Up or Down 5m Down Maker не сделали ни одной ставки?

Result:
Checked production PostgreSQL read-only on host `192.168.0.101`. Confirmed Service is `Running`/`Live` on build `info=1.0.0+229ce8ec60e523e95fe833a3ad8071874b43229e`, both Maker strategies are enabled and actively writing skipped `strategy_market_paper_runs`, but Paper orders are `0`. Root cause: every new-maximum Maker candidate was skipped, mostly by the global `opposite_outcome_open_order` guard because other BTC 5m Paper BUY orders already existed on the opposite outcome for the same condition; remaining candidates were skipped as `maker_expiration_elapsed` because the new max arrived after `market_end_utc - 60s`. No DB writes, service restart, live order submission, cancel action, or source-code changes were performed.

## 2026-05-21T03:32:17.0476056Z
Request:
Не понимаю. Если BTC Up or Down 5m Up Maker блокируется текущими ставками, значит должен ставится BTC Up or Down 5m Down Maker и наоборот.

Result:
Clarified with production DB examples that Maker sides are independently edge-triggered and do not switch to the opposite side when one side is blocked. On `btc-updown-5m-1779333600`, Up Maker had `19` new-max attempts from best ask `0.66` to `0.99`, all blocked by a Down Paper BUY from `btc_up_down_5m_binance_15s`, while Down Maker had `0` new-max events. On `btc-updown-5m-1779333300`, Up Maker was blocked by Down Paper BUY orders around `03:15-03:16Z`; Down Maker only got new maxima at `03:19:44Z`, `03:19:47Z`, and `03:19:54Z`, after the `03:19:00Z` Maker cutoff, so it skipped as `maker_expiration_elapsed`. No DB writes, service restart, live order submission, cancel action, or source-code changes were performed.

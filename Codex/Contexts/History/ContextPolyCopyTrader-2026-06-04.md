## 2026-06-03T21:09:38.7428175+00:00
Request:
Re:  Dashboard теперь грузит до 10_000 последних Paper/Live orders для вкладок orders. Мне не нравится это решение. Пусть грузит только первую страницу, но с учётом выбранной стратегии (или без неё, если выбрано All)

Result:
Reworked Dashboard order loading to use first-page server-side strategy filtering instead of preloading 10,000 rows. `Paper orders` and `Live orders` now load 100 recent rows by default for `All strategies`; when a strategy is selected, PostgreSQL filters by `strategy_id` before applying `ORDER BY created_at_utc DESC LIMIT 100`. Dashboard full refresh passes the selected filters, and changing an orders-tab selector reloads only Paper/Live order rows. Updated docs and added regressions for SQL/query behavior and test-repository filtering. Verification passed: Dashboard Verify build succeeded with existing Storage nullable warnings and no errors, StorageTests 38/38, full tests 585/585, and `git diff --check` clean except LF/CRLF warnings.

## 2026-06-03T21:43:19.0165005+00:00
Request:
Не вижу фикса. По-прежнему пусто для стратегии

Result:
Diagnosed the remaining empty selected-strategy orders view. Direct database checks showed that the local Dashboard database has 0 Paper/Live rows for the checked Instant strategies, while remote `192.168.0.101` has matching rows, including SOL Skip 42, ETH Skip 7, and BTC Middle 47. Fixed a Dashboard ViewModel race where selecting a Paper/Live orders strategy during a full refresh could leave the grid filtering the stale first global orders page without scheduling the server-side selected-strategy reload. Dashboard now queues a pending orders refresh and runs it immediately after the full refresh finishes. Verification passed: Dashboard Verify build succeeded with existing Storage nullable warnings and no errors, StorageTests 38/38, and full tests 585/585.

## 2026-06-03T22:00:55.7940721+00:00
Request:
Не работает

Result:
Found another Dashboard orders reload gap: if `Paper orders` / `Live orders` was clicked for the strategy that was already selected, the selected item value did not change, so the selector change handler did not run and the strategy-specific server reload was not requested. Updated row-button navigation to explicitly request an orders refresh when the resolved selection equals the previous selection, and added the current storage source to the orders loading/loaded status message so the UI shows whether it loaded from `Local database` or `Remote database`. Verification passed: Dashboard Verify build succeeded with existing Storage nullable warnings and no errors, StorageTests 38/38, and full tests 585/585.

## 2026-06-03T22:32:38.0234660+00:00
Request:
Теперь показывает нормально, но очень долго. Может, индекс какой-нибудь добавить?

Result:
Checked remote PostgreSQL indexes and query plans for the Dashboard orders tabs. No new index was needed: `paper_orders` already has strategy/time indexes and the selected Paper query uses `ix_paper_orders_strategy_perf_cover`; selected order queries were fast on the remote database. The slow path was Dashboard order-only reload calling `GetStrategyPerformanceAsync(10_000)` only to resolve strategy names. Updated `DashboardDataService.LoadOrderRowsAsync` to use cached strategy names or static configured names instead, and added a regression that prevents the heavy strategy-performance reload from returning to the order-only path. Verification passed: focused StorageTests 39/39, Dashboard Verify build, and full tests 586/586.

## 2026-06-03T22:45:30.5169645+00:00
Request:
Проверь - ставки ставятся?

Result:
Checked the remote production database read-only. `PolyCopyTrader.Service` is running in `Live` mode with a fresh heartbeat. Paper orders are actively being created: latest Paper order was about 0.5 minutes old, with 277 Paper orders in the last 5 minutes and 404 in the last 15 minutes. Live orders are also being placed/matched, but only recently for `ETH Up or Down 5m Skip 7 bps Instant`: latest Live order was at `2026-06-03 22:36:02 UTC`, status `Matched`, response `matched`; there were 3 Live orders in the last 60 minutes. BTC Middle 47, ETH Skip 32, ETH Skip 7, and SOL Skip 42 are all still `live_stakes = true`, not paused, and not auto-live-paused. No API errors were recorded in the last 15 minutes.

## 2026-06-03T22:55:42.7649382+00:00
Request:
Посмотри картинку. Что означает ошибка Prelight rejected ?

Result:
Extracted the screenshot from the Windows clipboard and confirmed the selected row is `PreflightRejected`. Queried the matching remote `live_orders` row at `2026-06-03 20:45:13 UTC`: strategy `ETH Up or Down 5m Skip 7 bps Instant`, no `order_id`, `response_status = preflight_rejected`, and `validation_summary = Maximum open live order count reached.` Explained that this is a local service preflight rejection before sending anything to Polymarket, not an exchange rejection. The row was historical from before the restart/deploy on `fbee408 Remove global live order count cap`; the running service started at `2026-06-03 20:48:57 UTC`, and the next visible Live row at `20:50:08 UTC` was `Matched`. No code or production state was changed.

## 2026-06-04T05:42:03.9677476+00:00
Request:
Что означает поле Filled в Live orders ?

Result:
Read the Dashboard Live orders bindings and model mapping. The `Filled` column is bound to `LiveOrderRow.FilledSize`, mapped from `LiveOrder.FilledSize` / `live_orders.filled_size`. It is the number of outcome shares/contracts that have actually matched on Polymarket, not a dollar amount. The dollar value is shown separately as `Filled $` / `FilledNotionalUsd`, and the unfilled share quantity is shown as `Remaining`.

## 2026-06-04T06:26:34.2812397+00:00
Request:
Объясни по стратегии ETH Up or Down 5m Skip 7 bps Instant. Почему при Live won и Live lost одинаковых, по 124, стратегия всё равно в плюсах?

Result:
Read the Dashboard/Storage live-performance aggregation and checked the remote production database read-only. `Live won/lost` are simple counts of settled Live orders, while `Live PnL` sums `live_orders.realized_pnl_usd`, so equal counts can still be profitable when average winning PnL is larger than average losing PnL. For the user's 124/124 snapshot, `ETH Up or Down 5m Skip 7 bps Instant` had total win PnL `+384.920863`, total loss PnL `-372.909386`, and net `+12.011477`; average win was about `+3.1042`, average loss about `-3.0073`. Current data had moved to 124/125 after a later `-4.00` settlement at `2026-06-04 06:21:26 UTC`, leaving net PnL about `+8.011477`.

## 2026-06-04T06:32:12.6368186+00:00
Request:
Реальная сумма ставки в Live stakes в поле Notional ?

Result:
Read the Dashboard Live orders bindings and service accounting code. `Notional` in the Live orders grid is `live_orders.notional_usd`: the intended/submitted order amount, effectively `price * requested shares` after strategy sizing and rounding. It is not always the real spent amount. The actual executed value is `Filled $` / `FilledNotionalUsd`; with fees included it is `Cost basis`. For fully matched orders these values are usually close to `Notional`, while for unfilled, cancelled, partial, or preflight-rejected rows `Notional` can be nonzero even though the actual executed stake is lower or zero.

## 2026-06-04T06:51:53.0434979+00:00
Request:
Почему по ETH Up or Down 5m Skip 32 bps Instant за последний час было 2 paper ордера, но ни одного живого?

Result:
Checked the remote production database read-only and the live-shadow allowlist in code. At database time `2026-06-04 06:51:35 UTC`, `ETH Up or Down 5m Skip 32 bps Instant` had `live_stakes = true`, `auto_live_paused = false`, and `paused = false`. The database showed three Paper orders in the last 60 minutes by then (`05:55:08`, `06:11:04`, `06:40:18 UTC`), but all had null `correlation_id`, empty `execution_source`, and no shadow metadata; there were zero `paper_live_shadow_decisions` and zero `live_orders` for that strategy in the same window. The cause is code-side gating: `BtcUpDown5mPaperStrategyProcessor.ShouldRunPaperLiveShadowTest` requires the variant to be in `PaperLiveShadowAllowedVariantCodes`, and that list includes `eth_up_down_5m_skip_bps_7_instant` but not `eth_up_down_5m_skip_bps_32_instant`. Live placement therefore was not attempted; it was not a preflight rejection or auto-pause.

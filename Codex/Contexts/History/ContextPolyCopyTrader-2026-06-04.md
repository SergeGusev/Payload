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

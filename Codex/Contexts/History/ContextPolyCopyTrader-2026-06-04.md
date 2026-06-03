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

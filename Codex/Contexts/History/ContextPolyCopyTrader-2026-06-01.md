## 2026-06-01T20:29:05.0726122+00:00
Request:
Меня не было неделю. Сервер работал. Проверь, собирал ли он корректно статистику всю неделю

Result:
Checked production PostgreSQL read-only through `out\dbprobe` with host override `192.168.0.101`. Service is running and fresh, Paper activity continued, and Live remained disabled. Found that `btc_up_down_5m_statistics_ticks` has collected no rows since `2026-05-19T05:18:12Z` because `btc_up_down_5m_statistics` is disabled in the production `strategies` table. Also found a `2026-05-27T10:47Z` to `2026-05-28T07:04Z` external connectivity incident where BTC/ETH/SOL odds archives stopped for about `20h17m`, with many Polymarket/Binance DNS/connectivity/timeout errors.

## 2026-06-01T20:40:55.1488211+00:00
Request:
Появились стратегии - кандидаты на Live ?

Result:
Checked production PostgreSQL read-only through `out\dbprobe` with host override `192.168.0.101`. Confirmed there are still `0` manual/effective Live strategies. Current explicit Paper/Live-shadow allowlist has no recent qualifying settled Paper candidates. Outside the allowlist, found `304` positive 7d Paper-only candidates with at least `30` settled rows and positive all-time PnL; `187` also positive over 24h; `122` strict candidates positive over 7d, 24h, and 12h. Top strict candidates were mostly BTC Middle Instant and SOL Middle/Skip Instant variants.

## 2026-06-01T20:50:10+00:00
Request:
Restore the Dashboard errors tab so errors can be caught and saved.

Result:
Restored `Dashboard Errors` visibility in the default strategies-only Dashboard, added `Save errors`, and added `DashboardErrors.csv` export under a timestamped `*-dashboard-errors` folder in `Analytics:CsvExportDirectory`. Updated README/config docs. Verification passed: full test project 550/550, Dashboard build to a temporary output directory with 0 errors, and `git diff --check`; normal Debug output build was blocked by the currently running Dashboard/Visual Studio DLL locks.

## 2026-06-01T21:01:45+00:00
Request:
Посмотри картинку. Как победить эту ошибку?

Result:
Extracted the screenshot from the Windows clipboard. Diagnosed the error as `NpgsqlException: Exception while reading from stream` with inner `Timeout during reading attempt` during Dashboard `GetStrategyPerformanceAsync`, meaning PostgreSQL did not finish the strategy-performance aggregate within Npgsql's default 30-second command timeout. Added an explicit 180-second command timeout to all-time and recent strategy-performance queries and documented the troubleshooting path. Verification passed: focused StrategyPerformance tests 7/7, full test project 550/550, Dashboard temp-output build 0 errors, and `git diff --check`.

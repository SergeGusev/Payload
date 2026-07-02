## 2026-07-02T15:12:10.4902657+00:00
Request:
Жив ли сервер?

Result:
Checked production read-only. PostgreSQL `192.168.0.101:5432` is reachable. `PolyCopyTrader.Service` heartbeat is fresh and healthy: `Running`/`Live`, version `info=1.0.0+06ae3df...`, heartbeat age about `40s`, `last_error = null`. Paper activity is current with `216` orders in `5m`, `625` in `15m`, and latest Paper order at `2026-07-02 18:10:15+03`. Live activity is current with `3` orders in `5m`, `9` in `15m`, `30` in `60m`, latest Live order at `2026-07-02 18:09:30+03`; latest Live orders are matched FAK SOL orders with no validation summary error. `api_errors_10m=0`. No production writes or service actions were performed.

## 2026-07-02T18:32:36.2169650+00:00
Request:
Screenshot of Dashboard error: `NpgsqlException: Exception while reading from stream` / `Timeout during reading attempt` in `GetStrategyPerformanceAsync`.

Result:
Diagnosed the Dashboard timeout as a heavy remote strategy-performance aggregation, not a server outage. Production currently has about `8,182` strategies and `4.0M` `strategy_market_paper_runs`; the countertrend JSON scan over `paper_orders.raw_decision_json` alone took about `65.7s`, and `run_agg` took about `35s`. Added schema index `ix_paper_orders_countertrend_signal_perf` for Paper orders with `previous_score`/`previous_score_bps`, applied it online on production with `CREATE INDEX CONCURRENTLY` in about `2m39s`, and verified the countertrend scan now uses an index-only scan in about `0.05s`. The full `GetStrategyPerformanceAsync` SELECT now completes in about `1m45s`, under the current `180s` timeout. Added a focused schema test; targeted test passed `1/1` with existing nullable warnings. No service restart, strategy state change, Live enablement change, order submission, or cancel action was performed.

## 2026-07-02T18:45:27.2955133+00:00
Request:
Давай начинать работу таким образом, чтобы для выборок в Dashboard применялись плоские запросы на заранее подготовленные данные.

То есть нормальный цикл жизни сервиса такой:

1. Отслеживание временных событий начала рынка.
2. Постановка ставок.
3. Запись ставок в базу.
4. В паузу - подготовка предвычесленных данных для Dashboard

И в итоге Dashboard должен показывать уже подготовленные данные без долгих вычислений

Result:
Implemented the first Dashboard precompute slice for strategy performance. Added PostgreSQL table `dashboard_strategy_performance_snapshots`, a dedicated snapshot repository, and a service hosted worker that computes the existing heavy strategy-performance aggregation outside the UI and refreshes the snapshot every 10 minutes during a quiet slot about one minute after a 5-minute market boundary. Switched Dashboard strategy rows and `Strategies.csv` export to read the flat snapshot table. Added focused source/schema tests. Verification passed for service build, test-project build, `DashboardSnapshotTests` (`3/3`), and `StorageTests` (`49/49`). Full solution build was blocked by a running Dashboard process locking WPF output DLLs; full unfiltered tests still have unrelated existing `BtcUpDown5mPaperStrategyProcessorTests` failures.

## 2026-07-02T18:57:48.1201834+00:00
Request:
Объясни, почему снимок будет обновляться каждые 10 минут? Мы можем сделать отдельный поток с низким приоритетом, который гарантированно не будет мешать текущей работе сервиса, но при этом будет в бесконечном цикле обновлять снимок?

Result:
Explained that the first 10-minute cadence is a conservative throttle because the source snapshot refresh still uses the heavy all-history `GetStrategyPerformanceAsync` aggregation, previously measured around `1m45s` on production. Explained that the current implementation is already a separate hosted background worker, but a low-priority .NET thread cannot guarantee non-interference because the dominant shared resource is PostgreSQL CPU/I/O/cache/locks, not C# thread CPU. Recommended a low-impact loop with configurable cadence, database timeouts, a single refresh connection, operational-window skip/cancel logic, and eventually incremental precompute after order/run writes instead of repeated all-history aggregation. No code changes or production actions were performed.

## 2026-07-02T19:19:06.2532027+00:00
Request:
Проверяй

Result:
Verified the deployed production build read-only. PostgreSQL `192.168.0.101:5432` is reachable and `PolyCopyTrader.Service` is running commit `ea795e5` in `Live` mode with a fresh heartbeat and empty `last_error`. The new table `dashboard_strategy_performance_snapshots` exists and the first refresh populated `8182` rows at `2026-07-02T19:11:52Z`; `DashboardStrategyPerformanceSnapshotWorker` has `0` API errors in the last hour. A flat snapshot read over `SELECT * ... ORDER BY ... LIMIT 25000` returned/countable `8182` rows in about `441 ms` from this machine. Current activity continues: one sample showed `571` Paper orders and `3` Live orders in the previous 15 minutes. The separate production latency gate is still red after restart (`341/1833` rows over `3s`, then `398/1841` rows over `3s`), mostly skipped Diff/Shift/Progress rows around `3.5s-3.75s`; the first snapshot completed after the first affected entry slot, so this does not look caused by the snapshot refresh. No production writes, service actions, live toggles, orders, or cancels were performed.

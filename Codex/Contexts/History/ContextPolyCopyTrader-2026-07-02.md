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

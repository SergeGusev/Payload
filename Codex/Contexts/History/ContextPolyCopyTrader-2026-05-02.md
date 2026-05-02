## 2026-05-02T04:13:22.4128205Z
Request:
Посмотри лог, как там?

Result:
Reviewed `D:\1\polycopy-overnight-health-20260501-235733.log` and ran a live PostgreSQL health check. The monitor is still running and has logged checks `1/20` through `15/20`; all are `WARN`, with no monitor `ERROR` rows. Service heartbeat stayed fresh, `PolyCopyTrader.Service` remained `Running` in `ReadOnly`, `blocked=0`, `schema_active=0`, and the wallet-activity queue index exists. Warnings are from repeated refresh-worker stream timeouts/deadlocks and queue growth; token metadata queue fell from about 19.9k to 22, while position/activity/performance/category-performance queues grew.

## 2026-05-02T04:27:20.6252109Z
Request:
Ок, займись снижением конкуренции

Result:
Implemented contention reduction for on-chain derived refresh. Added a shared non-blocking PostgreSQL advisory transaction lock around activity, position, wallet-performance, and wallet/category-performance refresh cycles; changed `missing_activity` seeding to use `polymarket_onchain_wallet_fills`; stopped updating already-queued performance/category-performance rows on conflicts; and staggered/lowered refresh intervals, batch sizes, and seed sizes. Updated service/dashboard config defaults, domain defaults, README, and configuration reference. Verification passed: targeted tests 27/27, full tests 138/138, service build, dashboard build, and `git diff --check`.

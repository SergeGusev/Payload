## 2026-06-01T21:36:33Z
Request:
Maybe add indexes for long Dashboard queries?

Result:
Added conservative concurrent PostgreSQL schema indexes for the long Dashboard strategy-performance path: covered `paper_orders` indexes for strategy aggregation and fill joins, a covered `paper_fills(filled_at_utc, paper_order_id)` index for recent fill windows, and timestamp-first `strategy_market_paper_runs` indexes for recent `updated_at_utc`, `entered_at_utc`, and `settled_at_utc` windows. Kept existing indexes without dropping/rebuilding them, updated schema initializer parsing and schema coverage tests, and documented that all-time strategy grids may still need precomputed/materialized aggregation if they remain slow. Verification passed: focused Storage/StrategyPerformance tests 36/36, full test project 552/552, and `git diff --check`.

## 2026-06-01T21:48:53.6817806Z
Request:
Давай отменим Auto live pause для всех стратегий. И будем включать его для конкретных стратегий по необходимости

Result:
Made automatic Live pause opt-in through `LiveTrading:AutoLivePauseStrategies`, defaulting to an empty list so runtime pause/resume updates are skipped for every strategy unless its code/id is explicitly configured. Added config validation and a policy helper, guarded the Live settlement, shared Paper settlement, and BTC strategy settlement auto-pause update paths, and added one-time schema data migration `20260602_clear_auto_live_pause_by_default` to clear existing `strategies.auto_live_paused=true` rows on deploy. Updated appsettings, README, configuration reference, and tests. Verification passed: focused config/storage/live/paper settlement tests 74/74, full test project 556/556, Dashboard temp-output build 0 errors, and `git diff --check`.

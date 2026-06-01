## 2026-06-01T21:36:33Z
Request:
Maybe add indexes for long Dashboard queries?

Result:
Added conservative concurrent PostgreSQL schema indexes for the long Dashboard strategy-performance path: covered `paper_orders` indexes for strategy aggregation and fill joins, a covered `paper_fills(filled_at_utc, paper_order_id)` index for recent fill windows, and timestamp-first `strategy_market_paper_runs` indexes for recent `updated_at_utc`, `entered_at_utc`, and `settled_at_utc` windows. Kept existing indexes without dropping/rebuilding them, updated schema initializer parsing and schema coverage tests, and documented that all-time strategy grids may still need precomputed/materialized aggregation if they remain slow. Verification passed: focused Storage/StrategyPerformance tests 36/36, full test project 552/552, and `git diff --check`.

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
## 2026-06-01T22:01:38.8974388Z
Request:
Давай переведём в Live ETH Up or Down 5m Skip 7 bps Instant

Result:
Added `eth_up_down_5m_skip_bps_7_instant` to the explicit Paper/Live-shadow allowlist, added a regression test covering linked Paper-shadow and live GTD order creation for that strategy when `LiveStakes=true`, and updated README/configuration/live-checklist documentation. Verification passed: targeted ETH test 1/1, focused BTC processor/admin tests 162/162, full test project 557/557, and `git diff --check`. Production `LiveStakes` was not toggled before deploy because the running service must first be restarted on this allowlist commit.

## 2026-06-01T22:39:03.6739631Z
Request:
Выложил. Проверь

Result:
Verified production after the `ETH Up or Down 5m Skip 7 bps Instant` Live allowlist deploy. Production was running `2cd45f0`, mode `Live`, heartbeat fresh, geoblock OK, and the auto-live-pause clearing migration had run. Enabled `LiveStakes` only for `eth_up_down_5m_skip_bps_7_instant`; production then had exactly one effective Live strategy. Confirmed the Dashboard discrepancy: production `192.168.0.101` has the target Live flag on, while the default local `127.0.0.1` environment has zero Live strategies and no matching target row, so an unchecked Dashboard row indicates the wrong/stale connection rather than the production flag. Observed a 22:35 UTC live-shadow attempt for the target, but it was `PreflightRejected` and the paper-shadow order was cancelled because `API error lockout is active`. Root-caused the lockout to repeated Gamma active-market `HTTP 422` max-offset responses using the newer `offset too large, use /markets/keyset` text; fixed expected-error recognition in `PolymarketHttpClient` and `GammaMarketIngestionProcessor`, added tests for both old/new max-offset messages, and documented the behavior in README. Verification passed: focused Gamma/Polymarket client tests 43/43 and full test project 559/559. Production still needs redeploy of this fix, then the old API-error lockout can clear after the configured 15-minute window.

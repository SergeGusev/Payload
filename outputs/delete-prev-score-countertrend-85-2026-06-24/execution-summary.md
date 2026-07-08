# Delete BTC Prev Score Countertrend 85

Date: 2026-06-24

Target:

- id: `b7c50005-0000-4000-8025-000000000085`
- code: `btc_up_down_5m_prev_score_countertrend_85`
- name: `BTC Up or Down 5m Prev Score Countertrend 85`
- wallet: `strategy:btc_up_down_5m_prev_score_countertrend_85`

Environment:

- database: `polycopytrader`
- host override: `192.168.0.101`

Dry run:

- strategy rows: 1
- paper orders: 287
- paper fills: 286
- strategy market paper runs: 356
- paper positions: 286
- paper position settlements: 285
- signals: 287
- open/unsettled live orders: 0

Execute:

- strategy rows deleted: 1
- paper orders deleted: 287
- paper fills deleted: 286
- strategy market paper runs deleted: 356
- paper positions deleted: 286
- paper position settlements deleted: 285
- signals deleted: 287
- live orders deleted: 0
- shadow rows deleted: 0
- copied leader rows deleted: 0
- on-chain paper signal rows deleted: 0

Final verify-only:

- strategy rows: 0
- paper orders: 0
- paper fills: 0
- strategy market paper runs: 0
- paper positions: 0
- paper position settlements: 0
- signals: 0
- open/unsettled live orders: 0

Notes:

- The strategy was disabled and de-live-updated before dependent row deletion.
- No `error.txt` was produced by the cleanup utility.

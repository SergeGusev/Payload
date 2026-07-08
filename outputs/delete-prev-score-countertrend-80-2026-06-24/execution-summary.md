# Delete BTC Prev Score Countertrend 80

Date: 2026-06-24

Target:

- id: `b7c50005-0000-4000-8025-000000000080`
- code: `btc_up_down_5m_prev_score_countertrend_80`
- name: `BTC Up or Down 5m Prev Score Countertrend 80`
- wallet: `strategy:btc_up_down_5m_prev_score_countertrend_80`

Environment:

- database: `polycopytrader`
- host override: `192.168.0.101`

Dry run:

- strategy rows: 1
- paper orders: 288
- paper fills: 288
- strategy market paper runs: 357
- paper positions: 288
- paper position settlements: 286
- signals: 288
- open/unsettled live orders: 0

Execute:

- strategy rows deleted: 1
- paper orders deleted: 288
- paper fills deleted: 288
- strategy market paper runs deleted: 357
- paper positions deleted: 288
- paper position settlements deleted: 287
- signals deleted: 288
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

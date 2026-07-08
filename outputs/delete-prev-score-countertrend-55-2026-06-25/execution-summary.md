# Delete BTC Prev Score Countertrend 55

Target:
- id: `b7c50005-0000-4000-8025-000000000055`
- code: `btc_up_down_5m_prev_score_countertrend_55`
- name: `BTC Up or Down 5m Prev Score Countertrend 55`
- wallet: `strategy:btc_up_down_5m_prev_score_countertrend_55`

Production database:
- host override: `192.168.0.101`
- database: `polycopytrader`
- dry-run started: `2026-06-24T21:25:38Z`
- execute started: `2026-06-24T21:29:05Z`
- verify-only started: `2026-06-24T21:30:00Z`

Dry-run result:
- target strategy rows matching id/code/name: `1`
- open/unsettled live order guard: `0`
- paper orders: `330`
- signals: `330`
- strategy market paper runs: `406`
- paper fills: `268`
- paper positions: `268`
- paper position settlements: `267`
- live/shadow/copied-leader/onchain rows: `0`

Execute result:
- disabled/de-live-updated target strategy rows: `1`
- deleted strategy market paper runs: `406`
- deleted paper fills: `268`
- deleted paper position settlements: `267`
- deleted paper positions: `268`
- deleted paper orders: `330`
- deleted signals: `330`
- deleted strategy rows: `1`
- late target rows after refresh: `0`
- live/shadow/copied-leader/onchain rows: `0`

Verify-only result:
- target strategy rows matching id/code/name: `0`
- exact target row still in `strategies`: `0`
- paper orders: `0`
- signals: `0`
- strategy market paper runs: `0`
- paper fills: `0`
- paper positions: `0`
- paper position settlements: `0`
- live/shadow/copied-leader/onchain rows: `0`
- signal rejections: `0`

Artifacts:
- `result.txt` contains the final verify-only output because each utility run overwrites it.
- `error.txt` was not created.

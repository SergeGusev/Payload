# Delete BTC More 60 Gamma Below 70

Target:
- id: `b7c50005-0000-4000-8022-000000060070`
- code: `btc_up_down_5m_more_60_gamma_below_70`
- name: `BTC Up or Down 5m More 60 Gamma Below 70`

Production database:
- host: `192.168.0.101`
- database: `polycopytrader`

Dry run:
- target strategy row existed: `1`
- open/unsettled live-order guard: `0`
- target history found: `387` paper orders/signals, `398` strategy runs, `335` fills, `335` positions, `334` settlements

Execute:
- disabled/de-live-updated strategy rows: `1`
- deleted strategy runs: `398`
- deleted paper fills: `335`
- deleted paper position settlements: `334`
- deleted paper positions: `335`
- deleted paper orders: `387`
- deleted signals: `387`
- deleted strategies: `1`
- live/shadow/copied-leader/onchain rows: `0`

Verify-only:
- target strategy row: `0`
- paper orders: `0`
- strategy runs: `0`
- paper fills: `0`
- paper positions: `0`
- paper position settlements: `0`
- signals: `0`
- signal rejections: `0`
- live/shadow/copied-leader/onchain rows: `0`

Notes:
- `error.txt` was not created.
- `result.txt` contains the last verify-only run output because the utility rewrites it on each run.

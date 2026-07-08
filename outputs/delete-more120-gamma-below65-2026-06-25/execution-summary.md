# Delete BTC More 120 Gamma Below 65

Target:
- id: `b7c50005-0000-4000-8022-000000120065`
- code: `btc_up_down_5m_more_120_gamma_below_65`
- name: `BTC Up or Down 5m More 120 Gamma Below 65`

Production database:
- host: `192.168.0.101`
- database: `polycopytrader`

Dry run:
- target strategy row existed: `1`
- open/unsettled live-order guard: `0`
- target history found: `391` paper orders/signals, `402` strategy runs, `262` fills, `262` positions, `262` settlements

Execute:
- disabled/de-live-updated strategy rows: `1`
- deleted strategy runs: `403` including `1` late run
- deleted paper fills: `262`
- deleted paper position settlements: `262`
- deleted paper positions: `262`
- deleted paper orders: `391`
- deleted signals: `391`
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

# BTC More 150 Gamma Below 80 Cleanup

Target:
- id: `b7c50005-0000-4000-8022-000000150080`
- code: `btc_up_down_5m_more_150_gamma_below_80`
- name: `BTC Up or Down 5m More 150 Gamma Below 80`
- wallet: `strategy:btc_up_down_5m_more_150_gamma_below_80`

Production target: PostgreSQL host override `192.168.0.101`, database `polycopytrader`.

Dry-run counts before deletion:
- strategy rows: `1`
- paper orders: `312`
- paper fills: `246`
- strategy runs: `323`
- live orders: `0`
- open/unsettled live orders: `0`
- paper positions: `246`
- paper position settlements: `245`
- signals: `312`

Execution notes:
- First execute pass disabled the strategy row, then deleted `323` strategy runs, `246` fills, `245` settlements, and `246` positions.
- The first execute command was stopped after the shell timeout while retrying a heavy late-run scan. The process was stopped manually; no error file was produced.
- The cleanup utility was patched to avoid the heavy late-run `OR paper_order_id IN (...)` scan during resume.
- Resume execute deleted `1` late strategy run, `1` late settlement, `312` paper orders, `312` signals, and the strategy row.

Final verify-only result:
- strategy row: `0`
- paper orders: `0`
- paper fills: `0`
- strategy runs: `0`
- live orders: `0`
- open/unsettled live orders: `0`
- shadow decisions/discrepancies: `0`
- paper positions: `0`
- paper position settlements: `0`
- performance rows: `0`
- copied-leader rows/events: `0`
- onchain paper signal results: `0`
- signals: `0`
- signal rejections: `0`

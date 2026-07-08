# BTC More 60 Gamma Below 80 Cleanup

Target:
- id: `b7c50005-0000-4000-8022-000000060080`
- code: `btc_up_down_5m_more_60_gamma_below_80`
- name: `BTC Up or Down 5m More 60 Gamma Below 80`
- wallet: `strategy:btc_up_down_5m_more_60_gamma_below_80`

Production target: PostgreSQL host override `192.168.0.101`, database `polycopytrader`.

Dry-run counts before deletion:
- strategy rows: `1`
- paper orders: `319`
- paper fills: `301`
- strategy runs: `331`
- live orders: `0`
- open/unsettled live orders: `0`
- paper positions: `301`
- paper position settlements: `300`
- signals: `319`

Execution notes:
- First execute pass disabled the strategy row, then deleted `331` strategy runs, `301` fills, `300` settlements, `301` positions, `319` paper orders, and `319` signals.
- Strategy row deletion initially failed because one target `paper_orders` row was created or remained after the initial temp-table snapshot.
- The cleanup utility was patched to refresh target paper-order ids before final deletes and run an additional late paper-orders pass.
- Resume execute deleted `1` late paper fill, `1` late paper position, `1` late paper order, `1` signal, and the strategy row.

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

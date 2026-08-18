# PolyCopyTrader History — 2026-08-18 UTC

## 2026-08-18T05:20:59.152817Z
Request:
Проверь сервер и ставки

Result:
Strict read-only production audit confirmed the restart succeeded: Service was Running/Live on exact build `31acec45`, with advancing heartbeat, no last error, no blocking/waiting locks, and no recurrence of the long vacuum lock. In the fixed 60-minute window, all 1,545 Paper orders across BTC/ETH/SOL had one fill, zero linkage defects or late entries, and p95/max latency `2.948s/4.663s`. There were 1,449 settlements with Gross PnL `+$1,739.9823` and Net PnL `+$1,353.9478`; active overdue backlog, Live orders, and shadow decisions were zero. Dashboard, reference data, and current Polymarket WebSockets were current. Warnings remained: 74 mostly short feed/OKX/Copy-performance errors and one recovered settlement deadlock pair; no tick gap exceeded 60 seconds and 1,027 settlements followed the deadlock. Codex made no production mutation.

## 2026-08-18T18:38:25.404418Z
Request:
Повтори.

Result:
Repeated the full three-asset report at one fresh UTC cutoff using only ordinary strategy names without `Progress` and authoritative lifetime Settled Paper Net PnL after fees. Winners were BTC `BTC Up or Down 5m Down 25 Diff Premarket` (261 settlements, Net `+$143.58725811`), ETH `ETH Up or Down 5m 4 bps Reference Average Premarket` (2,188, `+$428.27733115`), and SOL `SOL Up or Down 5m Up 4 bps Reference Average Premarket` (1,352, `+$269.54677489`). Produced three matching-price cumulative Net charts with maximum-drawdown regions and three daily Net Excel reports. Independent production RR/RO and Decimal reconciliation passed 90/90 checks; artifact, Excel COM, OpenXML, and visual checks passed. No production mutation was made.

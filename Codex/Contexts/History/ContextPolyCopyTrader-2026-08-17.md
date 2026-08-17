# PolyCopyTrader History — 2026-08-17 UTC

## 2026-08-17T05:05:51.910942Z
Request:
Проверь сервер и ставки

Result:
Production was audited strictly read-only on `192.168.0.101:5432/polycopytrader`. Service was Running/Live on exact build `31acec45` with advancing heartbeat, no last error, no persistent blockers, fresh BTC/ETH/SOL references, Connected current Polymarket WebSockets, and a current Dashboard projection. In the fixed 60-minute window, 1,579 Paper orders produced 1,571 fills with zero linkage defects or late entries; p95/max placement latency was 1.898/3.824 seconds. There were 1,694 settlements with Gross PnL +$1,056.8042 and Net PnL +$679.6653; Live/shadow activity and active overdue backlog were zero. Warnings remain: intermittent reference/OKX errors, four recovered settlement deadlocks, and 7/13 recent exact Maker-GTD orders expiring as market-data-delivery evidence unavailable. The Maker timestamp precision fix is runtime-confirmed: 0 post-deployment `order_lifetime_mismatch`, with all three last-24-hour occurrences predating deployment. Codex made no production mutation.

## 2026-08-17T18:51:36.540948Z
Request:
Опять не могу запустить сервис. Посмотри, нет ли там блоков

Я логи не выкладывал. По базе можешь посмотреть?

Result:
Strict read-only PostgreSQL inspection found the exact startup blocker without using logs. Autovacuum PID 9128 held `ShareUpdateExclusiveLock` on `dashboard_strategy_recent_projection_facts` from `08:18:32Z`, while the current startup schema path requests `ShareRowExclusiveLock NOWAIT` on the same table. Service heartbeat had stopped at `08:19:03Z` and no Service database session remained. The vacuum was not hung: heap-vacuum progress advanced from 26.290% to 78.063%, then completed naturally at `18:51:13Z`. Final state had zero waiting/ungranted locks and zero blocking chains, so the user could retry startup. Codex did not cancel the vacuum, terminate a backend, or mutate production.

## 2026-08-17T20:59:17.8357256Z
Request:
Теперь сделай то же самое, но три раза - по лучшим (смотри по Net PnL) стратегиями для каждой из валют.

Result:
At the fixed UTC cutoff `2026-08-17T20:50:20.448477Z`, the best fully Net-accounted Settled Paper strategy for each asset was selected and independently reconciled against production: BTC `BTC Up or Down 5m 5 Child Progress ROI` (3,441 settlements, Net +$1,069.40598290), ETH `ETH Up or Down 5m 20 Diff Down Progress` (410, +$470.27736816), and SOL `SOL Up or Down 5m Up 3 bps Reference Average Premarket` (1,443, +$274.31102228). Three Net-only cumulative charts with matching asset-price overlays and maximum-drawdown regions, plus three verified daily Net PnL Excel reports, were created in `outputs/019f88ae-b840-74e1-9392-4f7b2ef076c0/top-net-strategies-by-asset-20260817-2050z`. Independent ranking, raw-row/Decimal/production, chart, artifact-tool, Excel COM, and OpenXML checks all passed. No production or product behavior was changed.

## 2026-08-17T21:22:48.0708909Z
Request:
Чорт, не предупредил. Progress не считаем.

Переделай для обычых стратегий. И на будущее - только обычные стратегии

SOL можешь не пересчитывать, он нормальный

Result:
The prior BTC and ETH Progress selections were withdrawn and replaced using the Dashboard's exact ordinary-strategy rule: exclude names containing `Progress` case-insensitively. At fixed cutoff `2026-08-17T21:09:14.826487Z`, the corrected winners were BTC `BTC Up or Down 5m Down 25 Diff Premarket` (261 settlements, Net +$143.58725811) and ETH `ETH Up or Down 5m 4 bps Reference Average Premarket` (2,155, +$391.07136904). Their Net-only cumulative charts with matching asset-price overlays/max-drawdown regions and their daily Net Excel reports were rebuilt and independently verified. The existing SOL PNG/XLSX were confirmed byte-for-byte unchanged. Future strategy reports default to ordinary/non-Progress strategies unless the user explicitly says otherwise. No production or product behavior was changed.

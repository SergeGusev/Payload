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

# PolyCopyTrader History — 2026-08-10

## 2026-08-10T05:49:30.6732629Z
Request:
Выложил, проверяй

Result:
Completed a strict read-only production verification of the exact six group-8224 paired Maker-GTD strategies on `192.168.0.101/polycopytrader`. The deployed build `69a249a86127f40e4277dc671a35964da613e9a0` contains fix `cc24fc6e`, heartbeat and runtime activity were healthy, and paired worker/processor errors were zero. At cutoff `2026-08-10T05:43:07.807875Z`, all `18` post-restart legs used paired v2 receipt freshness with zero skips; the first detailed cohort showed fresh `1..2ms` HTTP receipts despite `5.606..78.014s` venue timestamps and passed all contract checks. One ETH Up v2 leg had a full optimistic TouchNoDepth Paper fill at `6.13 @ 0.50`, while its Down peer remained resting at `0.49`. Exact-family Live orders were zero. No production writes, service actions, configuration changes, or trading actions were performed.

## 2026-08-10T06:50:57.7904571Z
Request:
Ты написал:

Важно: 78 старых v1-ордеров остаются Pending. Поскольку deployment перезапустил сервис, их прежняя continuity-сессия потеряна; последующие TouchNoDepth fills для них должны fail closed, после чего они завершатся по expiry.

Означает ли это, что при каждой перезагрузке сервиса стратегия будет теряться?

Result:
Verified that the strategy and persisted records survive restart, but every already-resting paired Paper order loses eligibility for all subsequent TouchNoDepth fills because each process has a new confirmed-subscription session ID. Such an order is not recreated; it remains Pending until effective expiry, then becomes Expired and its run becomes Skipped with `maker_gtd_evidence_unavailable`. Future markets, recoverable Observed continuations, and already-entered positions continue normally. Therefore the strategy is not lost globally, but each pre-restart resting order is effectively lost as a fill candidate. No code, production, service, database, or trading change was made.

## 2026-08-10T07:46:52.9285993Z
Request:
Исправляй. Я в день по три раза перевыкладываюсь, меня такой вариант не устрастраивает. Лудно - пропустим пару тиков. Но чтобы вообще остановиться и ждать завершения - так нельзя

Result:
Implemented restart-safe gap recovery for the exact six group-8224 paired Maker-GTD Paper legs. Restart, owning-shard reconnect, asset reassignment, or delivery failure now creates a new immutable observation fence: the first authoritative frame after recovery is deliberately ignored for fill inference, and only a later authoritative event in the same unchanged segment may fill. Missed, cached, REST, pre-fence, and fence-frame observations are never backfilled. Existing exact-family v1/v2 orders and continuations remain eligible under this policy; new placements use v3. Recovery before expiry permits ordinary expired-unfilled completion, while absent or invalid recovery remains evidence-unavailable. Expanded changed-path tests passed 173/173, supporting tests passed 68/68, and the solution built with zero errors and one pre-existing nullable test warning. No deployment, production/service/database action, Live order, venue Paper order, or cancellation was performed.

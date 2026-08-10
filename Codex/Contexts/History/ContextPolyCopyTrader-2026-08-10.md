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

## 2026-08-10T08:13:34.6585597Z
Request:
Выложил, проверяй

Result:
Completed a strict read-only deployment audit of the exact six group-8224 paired Maker-GTD strategies on `192.168.0.101/polycopytrader`. The running service uses exact commit `c78eaee6110bbf70e60c4cad6251ef52c06d4ee8`, heartbeat advanced, all six rows remained Paper-only with zero Live orders, and relevant API errors were zero. At fixed cutoff `2026-08-10T08:11:37.550215Z`, all 251 carried Pending orders remained present (78 v1, 173 v2), while 18 new v3 orders formed nine correct equal-share `0.50/0.49` pairs with zero audited contract/linkage mismatch. A real critical WebSocket reconnect occurred and new v3 acceptance continued on the next generation. No post-start fill or expiry occurred, so actual old-order post-restart fill and persisted fence/no-backfill terminal evidence remain runtime-unobserved rather than disproved. All database transactions were read-only and rolled back. During diagnostics the DB password was exposed to an internal tool log; no system was changed, but the credential should be rotated.

## 2026-08-10T17:46:04.9598540Z
Request:
Повтори

Result:
Repeated the exact `ETH Up or Down 5m 2 bps Reference Average Premarket` report from a fixed production `REPEATABLE READ / READ ONLY / UTC` snapshot at `2026-08-10T17:32:54.723734Z`, using only persisted settled Paper Net PnL. Reconciled `2,512` unique runs/markets, Net PnL `$294.28902997`, fee-inclusive Net ROI `1.888804915866765167%`, Net W/L/flat `1,413/1,099/0`, latest UTC day `79 / -$81.17331006`, and maximum Net drawdown `$144.07010111`. Produced and visually verified a solid-line cumulative Net PnL chart with ETHUSDT overlay and the maximum drawdown region, plus a one-sheet daily Net PnL workbook for `38` continuous UTC dates with exact totals, red-on-white negative values, visible minus signs, and exact `B2` freeze panes. Production and trading state were unchanged; no replay was run. During the independent audit the production database credential appeared in an internal Codex tool log and should be rotated separately.

## 2026-08-10T19:10:14.1448612Z
Request:
Посчитай за последний месяц по этим трем рынкам количество результатов Up и количество Down

Последние 30 дней

Result:
Counted official BTC, ETH, and SOL Up/Down five-minute outcomes for the rolling UTC market-end window `[2026-07-11T18:57:02Z, 2026-08-10T18:57:02Z)`, fixed from the official CLOB server clock. The expected grid was `8,640` markets per asset. Final official resolved counts were BTC `4,263 Up / 4,376 Down`, ETH `4,277 Up / 4,362 Down`, and SOL `4,255 Up / 4,384 Down`, combined `12,795 Up / 13,122 Down`. Coverage was `25,917/25,920`: the exact BTC, ETH, and SOL markets starting `2026-08-05T14:55:00Z` remain officially open/unresolved and were excluded. Two closed-list omissions, one BTC and one ETH at `2026-07-12T02:25:00Z`, were restored only after their exact official detail endpoints independently confirmed resolved `Down`. Full alternate Gamma pagination passes agreed with zero duplicate, conflict, or invalid resolved rows. Production and trading state were unchanged.

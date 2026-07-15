# PolyCopyTrader History 2026-07-15

## 2026-07-15T06:13:18.8403477+00:00
Request:
System.ExecutionEngineException
  HResult=0x80131506
  Source=<Cannot evaluate the exception source>
  StackTrace:
<Cannot evaluate the exception stack trace>

Result:
Inspected the local Dashboard/Visual Studio runtime state, project source, build, logs, Windows events, WER/CrashDumps, and the installed .NET runtime. The initially observed debug process subsequently exited without a usable stack or dump, and the pasted exception could not be independently tied to that exact process, so its cause cannot be established. The restarted Dashboard remained responsive through a complete 60-second refresh interval, emitted no Windows crash event, and the current Release source built with zero errors. No product or production state was changed; a minidump with heap, Call Stack, and Debug Output is required if the exception recurs.

## 2026-07-15T06:18:05.3742903+00:00
Request:
Проверь ещё раз - вижу, что сервис работает

Result:
Rechecked production read-only and confirmed the user's observation. `PolyCopyTrader.Service` is operational: its `Running`/`Live` heartbeat advanced by one minute between samples, BTC/ETH/SOL ticks advanced by approximately 50 seconds, attributed PostgreSQL sessions were present, and the repository diagnostic found 3,434 entered rows across 1,756 strategies in ten minutes with zero enabled Up/Down 5m entries over the 3-second target. The service started approximately four minutes after the previous availability check ended, so the earlier stale result and the current healthy result refer to different service runs. No production or product state changed.

## 2026-07-15T06:19:02.5381324+00:00
Request:
Проверь, всё ли в порядке с сервером, ставятся ли ставки, нужны ли доработки по ускорению

Result:
Audited production read-only. PostgreSQL and the application are operational; Paper orders are actively placed and reconciled, and five actual Live fills in the preceding 24 hours were independently matched against the official Polymarket Data API, while no new Live order occurred in the latest hour because the only Live strategy failed verified entry conditions. Core entry latency is currently acceptable, but the deployed settlement path causes 30–94 second resolution queue delays and repeated timeouts; committed fix `d2525d28` is present in HEAD but not deployed. Also found an empty copied-trader performance projection, a stable legacy backlog of 234 positive Paper positions on 66 resolved conditions, and intermittent critical-WebSocket closes. No production or application state was changed.

## 2026-07-15T07:22:56.2096627+00:00
Request:
Выложил, проверяй

Result:
Verified the new production deployment strictly read-only. The running service reports exact commit `602d921d` and matching Release MVID prefix, remains live, and actively created fully reconciled Paper orders/fills/runs with zero enabled Up/Down 5m entries over the 3-second target; no new Live order occurred because no qualifying Live signal was produced. The settlement fix reduced equal-window queue delay from old p50/max `31.740/91.273s` with six errors to `1.801/8.233s` with zero errors. Four boundaries produced `1,399` matching settlements and zero positions in the final snapshot, but one boundary exposed a stale mark-write race that temporarily restored `233` settled positions for `174..214s`. Remaining work is to guard mark updates against settlement, add missing functional lookup indexes, implement an incremental copied-trader performance projector because the current table is empty and its worker is disabled, investigate two recovered critical-WebSocket closes, and separately repair the unchanged `234`-position legacy backlog. No production or product state changed.

## 2026-07-15T07:55:31.2624584Z
Request:
Ок, приступай к доработкам

Result:
Implemented the three verified code fixes: conditional single/batch mark-only writes that cannot restore a settled position, partial covering functional settlement lookup indexes, and a bounded dirty-wallet copied-performance projector outside the settlement path. Added transactional queue/control triggers, a registered 25-wallet worker with 100-wallet lexical reconciliation seeding, configuration/docs, deterministic race tests, and real disposable-PostgreSQL coverage. Release solution build passed with zero errors and one pre-existing warning from an unchanged test file; focused tests passed 124/124; PostgreSQL integration passed 4/4; the full suite remained at the exact saved baseline failure set (720 passed, 112 known failures, zero new or missing failure names). The exact disposable database was dropped and verified absent. No production state or deployment was changed.

## 2026-07-15T08:48:45.7347464+00:00
Request:
Выложил, проверяй

Result:
Verified production deployment `7e83d11c7b3ae4c6b252c6f2555625ff74ebd5c9` read-only. The service remained `Running` / `Live` with advancing heartbeat, attributed sessions, and BTC/ETH/SOL ticks. A fixed sample reached 932 post-start Paper orders; all checked orders were Filled with exact one-to-one fills/runs. The only Live-enabled strategy was correctly filtered before placement at 08:40 UTC, so no Live order was expected. Both new settlement indexes were valid and used by exact plans in 0.575–5.025ms. Across 1,174 settlements / 11 conditions and three full boundaries, every matching position remained zero and the former 174–214s stale positive restoration did not repeat, although a transient shorter than the maximum 148.806s sampling gap cannot be excluded. The copied-performance projector produced correct independently recomputed values and exact 25-wallet batches, but its queue grew from 1,085 to 1,674; 422 high-priority wallets remained and the oldest waited about 648s, so freshness needs further optimization. Entry latency also exceeded the 3s gate for 44 of the first 605 entries (max 4.133s) at two full boundaries, while the next boundary passed with max 1.621s. Four recovered critical-WebSocket remote closes and nine OKX timeouts remain operational issues. No production or product state was changed; repository changes are limited to required context/history records.

## 2026-07-15T10:24:09.6001924Z
Request:
Ок, продолжай с доработками

Result:
Implemented three evidence-backed improvements: the copied-trader projector now reserves separate `25` high-priority and `5` reconciliation slots and runs on a fixed cadence; all three due-entry workers use a serial fixed-rate loop without completion-plus-delay drift; and critical-WebSocket disconnects now retain safe structured diagnostics without raw endpoint query, exception, or close-description text. Read-only production evidence showed that prerequisite-result availability and later decision work can still dominate the `3s` entry gate, so no latency guarantee is claimed before deployment measurement. Focused tests passed `133/133`, disposable PostgreSQL tests passed `4/4` with verified database cleanup, the Release solution built with zero warnings/errors, and the full suite retained the exact known `112`-failure baseline with `734` passing tests and no new failure names. No production state or deployment was changed.

## 2026-07-15T11:56:02.2175186Z
Request:
Выложил, проверяй

Result:
Verified exact production deployment `e7bff261f9b95e4e1dafb9da2d49c92023f546d6` strictly read-only. The service remained healthy; `578` actual Paper entries reconciled exactly and all stayed below the `3s` gate (max `1.542s`). No Live order was attempted because the sole Live-enabled strategy hit the same verified pre-signal business skip at all three observed boundaries. The projector cadence improved to about `59.182` queue wallets/min versus the prior `34.302`, and high-priority backlog/age fell, but repeated runtime lock graphs proved its `11..14s` transaction holds queue rows and blocks Paper-position writes for up to at least `11.243s`; this is the next code target. All `460` checked settlements ended at zero without relevant errors. One critical WebSocket premature close exercised the safe diagnostic, recovered in `2.333s`, and was followed by `2,112` frames with zero parse failures. No production state or product code changed; only this required audit record was added.

## 2026-07-15T12:28:02.5267852Z
Request:
Ок, действуй

Result:
Implemented the copied-performance queue-lock fix with a short durable pending-to-inflight claim transaction and a separate long aggregation transaction under one session advisory lock. New events no longer wait on the projector's queue-row lock, interrupted work is recovered before fresh work, and seed/cursor/projection/inflight acknowledgement remain atomic. Added deterministic PostgreSQL blocker-graph, concurrent settlement, cancellation/recovery, quota-precedence, and cleanup coverage; updated README and configuration documentation with downgrade handling. Disposable PostgreSQL tests passed `6/6` with verified database cleanup, focused tests passed, Release build completed with zero errors, and the final full suite retained the exact known `112`-failure baseline (`736` passed, zero new/missing failure names). No production state or deployment was changed.

## 2026-07-15T18:50:47.8689003+00:00
Request:
Сделай Excel отчёт по Child и Child ROI стратегиям по всем трём валютам. На одной вкладке.

Столбцы - стратегии с самым высоким Pnl в рамках валюты и типа, всего получится шесть стратегий, в отчете их нужно сгруппировать от самого низкого Pnl к самому высокому.

В строках - даты
В ячейках - Paper Pnl за эту дату. Отрицательные значения выделяются красным на белом фоне со знаком минус.

Последний столбец - сумма по дню, последняя строчка - сумма по категории. На их пересечении - общий Total

Первую строчку и первый столбец закрепи, чтобы не пропадали при скролллинге

Result:
Created a refreshed one-sheet Excel report from a production `REPEATABLE READ, READ ONLY` snapshot at `2026-07-15T18:47:05.887967Z`. Evaluated 87,451 settled Paper rows across all 144 non-Progress BTC/ETH/SOL Child and Child ROI candidates, independently matched all aggregates, six winners, and daily values against server SQL, and sorted the six selected columns by total PnL ascending. Excel verified one sheet, exact formula-driven daily/category/grand totals (`$1,508.43229126`), 63 numeric financial cells, 9 red-on-white explicit-minus negative cells, zero formula errors, and frozen first row/column. The final workbook was visually inspected and saved in the ignored output directory; production and product code were unchanged.

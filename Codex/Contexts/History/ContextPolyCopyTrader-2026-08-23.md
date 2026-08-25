# 2026-08-23

## 2026-08-23T06:11:02.191920Z
Request:
Сможешь исправить ситуацию с Maker‑GTD  ?

Result:
Focused read-only production and source diagnosis proved that exact ETH Maker-GTD evidence timeouts are caused by queue waiting: the last 100 timeout expirations had matching event ages of 82,156.7825..261,849.3178 ms and total pending snapshots up to 157,760, while individual in-flight phases generally completed in milliseconds. The configured per-asset maximum is only a soft diagnostic threshold, and all events carrying open-order IDs remain nonreplaceable on one global worker that also performs position-mark work. Draft contract `RC-20260823-maker-evidence-fast-lane` validated at `sha256:dd8e0fb72f6ea8619229439d6820d6347cf6d0d218a6821fa62074cb0ff0fee1`; no product or production mutation was made pending explicit approval.

## 2026-08-23T05:57:03.613908Z
Request:
Проверь сервер и ставки

Result:
Read-only production verification confirmed build `a28aff1d...` Running/Live with fresh heartbeat/reference data, connected current WebSockets, 3,105 Filled Paper orders across BTC/ETH/SOL in the fixed last hour, no active overdue runs, no expired open orders, and no persistent PostgreSQL locks. Copied-trader performance projection recovered without restart and had zero inflight rows; its last stream-read error was `05:41:26.114543Z`. Exact ETH Maker-GTD remained degraded: 28 fills versus 27 timeout-expirations in the hour, with persisted side-effect queue snapshots up to 157,760 global and 66,044 matching updates. Sixteen Live orders in the last 24 hours were all Matched. No production or product mutation was performed.

## 2026-08-23T07:04:26.5924843Z
Request:
APPROVE RC-20260823-maker-evidence-fast-lane sha256:dd8e0fb72f6ea8619229439d6820d6347cf6d0d218a6821fa62074cb0ff0fee1

Result:
Implemented the approved exact ETH Maker-GTD evidence fast lane without changing strategy, pricing, stake, TouchNoDepth, accounting, mandatory label, PaperOnly, or Live behavior. The dedicated lane retains every eligible event FIFO with original timestamps, fingerprint, source id, and raw payload, remains independent of blocked general position-mark work, participates in expiry drain, and exposes separate pending/in-flight telemetry. General quotes coalesce only after Maker IDs are separated and no ordinary Paper order needs the event. Final tests passed 81/81 focused and 115/115 contract/WebSocket; the 160,000-event stress preserved exact FIFO/fingerprint/RawJson evidence. Full Debug solution build passed with 0 errors and 126 pre-existing warnings, none in changed files. Independent semantic review passed with no open findings. No production, database, service, configuration, schema, migration, subscription, or deployment state was changed.

## 2026-08-23T07:06:33.2505632Z
Request:
APPROVE RC-20260823-maker-evidence-fast-lane sha256:dd8e0fb72f6ea8619229439d6820d6347cf6d0d218a6821fa62074cb0ff0fee1

Result:
The completed implementation commit `fee67dcb9e90742f9d1bccf415a80f026fc61504` and its separate approval parent `644854b5` passed requirement-gate Range validation as two commits and were pushed to `origin/codex/reference-average-available-windows`; local and upstream heads matched with divergence `0/0`. The unrelated pre-existing August 20 history edit remained unstaged and untouched.

## 2026-08-23T07:25:17.4043292Z
Request:
При старте сервиса локально вот такая ошибка. Логи лежат здесь - D:\My\Business\PolyMarket\src\PolyCopyTrader.Service\bin\Release\net10.0\logs

Почини, пожалуйста

Result:
Read-only diagnosis proved the current `7b255d69` Release binary intentionally fails before host start because local `127.0.0.1:5432/polycopytrader` is a nonempty July-era legacy database with 576 objects/89 tables, no migration history, and no current projection relations or terminal triggers. The database contains substantial local history. Replaying the existing immutable schema would also execute five missing embedded cleanups; exact current targets comprise 27 retired strategies with 334 runs, 11 Paper orders/fills/signals/positions, and zero Live orders. No database, service, configuration, environment, production, or product-source mutation was made; user choice between an in-place upgrade and a new empty local database is required before the implementation contract can be fixed.

## 2026-08-23T07:55:28.7340766Z
Request:
1 - обновляем базу

Result:
The user selected the in-place local database upgrade. Draft contract `RC-20260823-local-legacy-database-upgrade` fixes the target to `127.0.0.1:5432/polycopytrader`, requires an exact fresh preflight, permits only the immutable baseline's currently verified cleanup of 27 retired strategies and their exact dependencies, excludes production/product-code/full-service-start/backup work, and records the non-transactional partial-upgrade risk. The draft validated at `sha256:21c2054c07118fcc889de070ba987acc8bdb21f84a4f01bfa4e1c43348e03d36`; no database or product mutation was made pending exact approval.

## 2026-08-23T08:04:13.9509687Z
Request:
APPROVE RC-20260823-local-legacy-database-upgrade sha256:21c2054c07118fcc889de070ba987acc8bdb21f84a4f01bfa4e1c43348e03d36

Result:
Recorded exact user approval of the unchanged local legacy database upgrade contract. The approval-only repository checkpoint is prepared before any database mutation; execution remains conditional on a fresh exact read-only local fingerprint and dependency preview.

## 2026-08-23T10:42:42.4815925Z
Request:
Опять ошибка

Result:
The service was expected to continue showing the same startup failure because the approved database upgrade had not yet run. Approval-only checkpoint `c24ea95c` passed Range validation. Fresh UTC read-only preflight matched the exact local endpoint, legacy fingerprint, 27 targets, and core counts, but found additional target dependencies omitted from the first contract: one Paper settlement, ten diff-state rows, 27 lifetime Dashboard snapshots, and 81 recent Dashboard snapshots. All checked Live, paired-family, and other dependency counts were zero. The prior approval was invalidated before any database write and the contract was revised for exact reapproval; production and the local database remained unchanged.

## 2026-08-23T10:48:25.9645417Z
Request:
APPROVE RC-20260823-local-legacy-database-upgrade sha256:c3588c790af3e8216c805a04cccbadbf330a8f76952a2f49f31e98483387668b

Result:
Recorded exact user reapproval of the revised local legacy upgrade contract including the complete proven dependency blast radius. A separate reapproval checkpoint is being created before the final exact read-only gate and any local database mutation.

## 2026-08-23T10:55:03.5258793Z
Request:
APPROVE RC-20260823-local-legacy-database-upgrade sha256:c3588c790af3e8216c805a04cccbadbf330a8f76952a2f49f31e98483387668b

Result:
Reapproval checkpoint `13ff61a2` preceded mutation. Two exact read-only gates passed at `10:52:49.582574Z` and `10:52:49.783592Z`, then all 789 immutable baseline statements completed. The approved 27 cleanup targets and dependencies were removed, but the post-gate discovered an unapproved seed effect: 1,440 enabled/unpaused strategies were created and 938 pre-existing strategy rows were updated, leaving 3,341 strategies. Execution stopped before baseline history registration; `schema_migration_history` exists with zero rows, the service still cannot initialize, and no further mutation occurred. Read-only audit at `10:54:16.414531Z` confirmed zero Live orders and zero runtime rows for the new strategies. Production was not accessed. A new user decision is required because no authorized backup/full rollback exists.

## 2026-08-23T10:58:13.1546021Z
Request:
Я не понимаю ни одного их предложенных действий. Объясни 1

Result:
Explained read-only that option 1 accepts the current post-baseline local database and adds only the missing immutable baseline ledger row, allowing later service startup to skip replay. The ledger insert itself does not start the service or create bets. A later manual start would make the 1,440 new enabled/unpaused, `live_stakes=false` strategies eligible for evaluation and Paper activity, so their enabled state remains a separate required decision. Exact prior values of the 938 touched pre-existing strategy rows remain unknowable without a pre-upgrade snapshot. No mutation was performed.

## 2026-08-23T10:50:32.2330065Z
Request:
А теперь сделай Progress стратегию LossDiff , основанную на логике подсчёта LossDif без очистки при победе, но при отсутсвии отрицательных значений, как мы делали в последнем графике.

Логика такая же, как в LossDiff N стратегиях, но размер ставки умножается на N

Когда я написал "сделай" - я имел в виду смоделируй и покажи её результат. Точнее, давай не одну LossDiff стратегию, а LossDiff N Progress, когда стратегия заходит только при N+ значениях

Нет, теперь давай 17

Result:
Completed a production read-only ResearchOnly replay for LossDiff N Progress, N=1..17, on the exact 1,053 settled parent rows. The counter increments on a loss, decrements on a win with a zero floor, and each N child enters causally at counter >= N with proportional N-times stake/gross/fee/Net. Independent PowerShell event replay and PostgreSQL window-event aggregation agreed on every result; maximum counter was 16, so N=17 had no trades. Delivered a visually verified PNG and interactive visualization; no database, service, trading, configuration, or product-source mutation occurred.

## 2026-08-23T10:57:04.3113040Z
Request:
Прямо мозг плавится. Я ожидал, что ROI будет плавно расти при N от 1 до 17. Где логика, брат?

Result:
Revalidated the same 1,053 production settled rows read-only and explained that multiplying every selected trade's stake and Net by the same N cancels exactly from ROI. Threshold sets are nested, and moving from N to N+1 removes the exact-N band; the measured exact-band ROI alternates sharply between positive and negative values, so the remaining weighted-average ROI necessarily jumps rather than rising monotonically. High-N estimates are additionally unstable because N=16 has one trade and N=17 has none. No production or product mutation occurred.

## 2026-08-23T10:59:55.5743324Z
Request:
А если какая-то из миграций не применится? Я давно не запускался

Result:
Verified from current source and persisted post-state that the catalog has only immutable baseline `0001`, not a chain of later pending versioned migrations. All 789 baseline statements already completed and all 14 embedded markers plus the pre-existing manual marker are present; the current blocker is the intentionally absent baseline ledger row after the catalog-seeding scope mismatch, not an unapplied historical migration. Explained that baseline registration must be preceded by comprehensive post-state verification because this legacy baseline is non-transactional. No mutation was performed.

## 2026-08-23T11:06:39.5210383Z
Request:
Ок, давай без Progress. Давай просто 17 стратегий LossDiff N Positive, которые ставят ставку если LostDiff равен или больше N

Result:
Completed a production read-only ResearchOnly simulation for LossDiff N Positive, N=1..17, using the exact parent stake with no Progress multiplier. The causal counter increments on a loss, decrements on a win with a zero floor, and each child enters at settled LossDiff >= N. Independent PostgreSQL window-event and PowerShell event-replay calculations agreed on all metrics. Delivered a visually verified PNG and inline chart/table; N=16 had one trade and N=17 none. No database, service, trading, configuration, or product-source mutation occurred.

## 2026-08-23T11:34:14.1095508Z
Request:
Сверь стуктуру Production и Local баз. Они совпадают?

Result:
Completed forced-read-only structural snapshots of exact local `127.0.0.1:5432/polycopytrader` and production `192.168.0.101:5432/polycopytrader`. They do not physically coincide: production has eight extra maintenance/recovery tables with 30 columns, 12 constraints, and 18 indexes, while local alone retains retired index `ix_paper_positions_wallet_updated`; 38 semantically identical columns also have different ordinal positions across five tables. Excluding only those maintenance tables, the retired index, and ordinal-only differences, all 2,755 remaining canonical application objects match with zero semantic changes. Independent catalog counts confirmed local 110 tables/376 indexes versus production 118/393. Migration state differs as well: local history is empty versus one production baseline row, and data-marker counts are 15 versus 18. No database, service, trading, configuration, schema, or source mutation occurred.

## 2026-08-23T11:45:32.1100931Z
Request:
Ок, тогда делай что нужно, чтобы сервис корректно запускался локально

Result:
Fresh forced-read-only preflight proved that exact local `127.0.0.1:5432/polycopytrader` already has every required relation and the complete one-entry baseline catalog state, but its migration history remains empty. It also reverified the unresolved catalog consequence: 1,440 baseline-created strategies remain enabled/unpaused with `live_stakes=false`, while all 3,195 enabled/unpaused strategies have zero live stakes. Drafted and validated `RC-20260823-local-startup-baseline-acceptance` with digest `sha256:f809e12010a1635a2d6c66d4d3d0372c081121803cf2d4ff4ba7c43e98c2bd2a`; it explicitly accepts/preserves that catalog, authorizes one baseline-ledger INSERT and a bounded normal local startup verification, and forbids Production, Live, schema, configuration, and product changes. No mutation occurred; exact user approval is required before continuing.

## 2026-08-23T17:36:53.9359957Z
Request:
Сделай две стратегии:

1. ETH 5m 1 Diff Confirmed Average Premarket LossDiff 4+

С той же логикой (подсчёт LossDiff c обрыванием после успеха), что и LossDiff 4+ выше

2. ETH 5m 1 Diff Confirmed Average Premarket LossDiff 13+ Positive

С той же логикой (подсчёт LossDiff без обрывания после успеха, но без ухода в минус), что и LossDiff 13+ Positive выше

Result:
Read-only implementation discovery resolved the exact parent and proved that the existing self-settlement `paper_lost_counter` cannot drive either requested parent-outcome gate. No existing durable parent-loss cursor exists. The exact local parent has zero settled history, while the previously simulated 1,053 rows belong to the currently unreachable server endpoint. Product edits are blocked pending the user's explicit choice between full-history restoration and zero-on-rollout initialization, plus actual-parent-entry versus independently reproduced parent-signal coupling. No product, database, service, strategy, order, configuration, or deployment mutation occurred.

## 2026-08-23T17:36:53.9359957Z
Request:
Re: при первом запуске LossDiff восстанавливается по всей доступной истории завершённых сделок родителя, а не начинается с нуля; &#x20;

Давай не будем вешать это на первый запуск. Запустим с нулём, а потом я попрошу тебя сгенерировать историю для обоих стратегий.

Re: дочерняя стратегия входит только если родитель фактически входит в тот же рынок; &#x20;

Да, в этом и есть её смысл.

Re: направление, цена и сумма ставки полностью копируются у родителя; &#x20;

Да, правильно.

Re: обе стратегии создаются включёнными для Paper, но с `live_stakes=false`. &#x20;

Да, правильно

Result:
Fixed the clarified behavior in draft contract `RC-20260823-eth-lossdiff-gated-children`: both counters are created once at zero with no historical replay, persist across ordinary restarts, count only post-cutoff outcomes of exact parent b7c50005-0000-4000-8204-000000000001, require that parent's actual same-market entry, copy its frozen direction/price/amount intent, and default enabled/unpaused with `live_stakes=false`. Historical generation and all database/deployment/Live mutations are reserved for later tasks. Mechanical validation passed at semantic digest `sha256:872f3ac0cf3005d63f025c4fd63ff2c1b95f6b36fb645f1427aed8bf2795a58c`; product edits remain blocked pending exact approval.

## 2026-08-23T17:38:30Z
Request:
`APPROVE RC-20260823-local-startup-baseline-acceptance sha256:f809e12010a1635a2d6c66d4d3d0372c081121803cf2d4ff4ba7c43e98c2bd2a`

Result:
Committed approval-only checkpoint `c1a7fc37`, then revalidated exact local `127.0.0.1:5432/polycopytrader` and inserted only baseline ledger row `0001-legacy-baseline-a3b0457f` under its advisory lock. Two initializer invocations skipped it. Exact Release build `7b255d69` started and produced two advancing healthy `Running/Live` heartbeats with `last_error=NULL` and zero Live orders; measured local catch-up created 3,783 runs, 81 Paper orders, one fill/position, and 56 settlements. The process exceeded its approved absolute limit by 8.539 seconds and five PostgreSQL read timeouts occurred when a 2,000-run direct skip-compaction request held the shared retention advisory gate beyond 30 seconds; service and PostgreSQL logs independently confirmed the chain, and the persistence queue recovered after one retry. The exact process was stopped. Approval was invalidated, and the contract was revised to a minimal second-start verification with digest `sha256:e77dcdc7d65bf85287201596f46285212bfc73eeb57dc2f087069f420eaba303`; no product/config/schema/Production/Live change was made.

## 2026-08-23T18:03:37.2936928Z
Request:
`APPROVE RC-20260823-local-startup-baseline-acceptance sha256:e77dcdc7d65bf85287201596f46285212bfc73eeb57dc2f087069f420eaba303`

Result:
Committed approval-only checkpoint `6b8879ba` and ran the approved second local start against exact `127.0.0.1:5432/polycopytrader`. Baseline initialization succeeded without replay: the sole ledger row remained exact, catalog counts remained 3,341 total and 3,195 enabled/unpaused with zero active `live_stakes`, and exact Release build `7b255d69` produced advancing healthy `Running/Live` heartbeats with `last_error=NULL` and zero Live orders. Runtime acceptance nevertheless failed independently: the exact interval contained 11 service Error events, 11 cancelled PostgreSQL service statements, two persistence retry/recovery sequences (`3/417` and `1/242` batches/runs), and repeated 30-second retention advisory-lock waits that blocked a WebSocket Paper-order update for about 30.038 seconds. PostgreSQL plans proved the production-v1 `DirectNewPaperSkipArchiveSql` duplicate check is quadratic: captured 2,000-row input compared 4,000,000 candidate pairs while holding the exclusive retention advisory lock. Source inspection proved every production caller already runs `EnsureUnambiguousDirectPaperSkipInput` before this SQL, making the SQL self-join redundant. The exact process was stopped, Live orders and remaining ungranted locks were zero. Due a yielded-session control delay, it ran 115.174 seconds after first healthy heartbeat, exceeding the approved 90-second interval by 25.174 seconds while remaining under the 120-second absolute limit; approval was invalidated. No product/config/schema/Production/Live change was made. Drafted the separate minimal fix contract `RC-20260823-direct-skip-compaction-linear-validation`, digest `sha256:64c66d662c534ca297dd09aa0756d094c00d64d3c40a26447c77c043df5448fc`.

## 2026-08-23T18:37:46.5283669Z
Request:
`APPROVE RC-20260823-eth-lossdiff-gated-children sha256:872f3ac0cf3005d63f025c4fd63ff2c1b95f6b36fb645f1427aed8bf2795a58c`

Result:
Implemented exactly two fixed children of `ETH Up or Down 5m 1 Diff Confirmed Average Premarket`: reset-on-win LossDiff 4+ and nonnegative Positive LossDiff 13+. Both begin once at durable zero under the new migration cutoff, count only exact-parent post-cutoff settled outcomes, preserve state across restarts, and never read historical parent rows at startup. A child is evaluated only after the exact parent actually enters the same market; eligible entries copy the parent's frozen outcome, amount, price cap, and FAK intent, while below-threshold decisions retain audit diagnostics. Both are seeded enabled/unpaused Paper with `live_stakes=false`; no database migration, service, deployment, historical generation, or real order action ran. Full solution build passed. LossDiff tests passed 10/10, controlled parent-entry/Live-parity tests 3/3, and migration/catalog tests 10/10 without a database connection. The final working-tree suite passed 1,394 and failed the same 216 pre-existing tests as a clean current-HEAD worktree, so the task added no full-suite failure. Independent semantic review passed with no blocking findings; PostgreSQL runtime integration remained intentionally unexecuted under the no-database-mutation scope.

## 2026-08-23T19:00:18.8916412Z
Request:
`APPROVE RC-20260823-direct-skip-compaction-linear-validation sha256:64c66d662c534ca297dd09aa0756d094c00d64d3c40a26447c77c043df5448fc`

Result:
Recorded approval-only checkpoint `d76cfc3a` before the product edit, then removed only the redundant production-v1 correlated JSON self-scan while preserving the mandatory C# ambiguity gate and all archive/accounting behavior. Focused verification passed 3/3, including an exact 2,000-run isolated PostgreSQL regression below the unchanged 30-second timeout. The complete retention class had 37 passes and 9 old failures; all 9 reproduced by exact name against unmodified `d76cfc3a`, so this correction added zero failures. Release solution build passed with zero errors. An isolated Release binary ran only against local `127.0.0.1:5432/polycopytrader`, produced two advancing healthy heartbeats, zero Error/Fatal, zero timeout/cancellation/retry, and zero Live orders, then exact PID 81548 was stopped within both approved timers. PostgreSQL's 11 short advisory-lock waits all acquired successfully; no locks or service connections remained. Independent semantic review passed after confirming the exact disposable PostgreSQL container was removed; no open findings remain. Implementation commit `f4ede380` passed isolated WorkingTree, Staged, Contract, and one-commit Range gates and was pushed to `origin/codex/reference-average-available-windows`. Production, schema, strategy/configuration, deployment, and venue state were untouched.

## 2026-08-23T19:21:55.982828Z
Request:
Идут ли сейчас ставки?

Result:
Production read-only verification confirmed active betting: exact current five-minute interval contained 28 new Paper orders from 28 strategies, all Filled, with latest order and linked entered run at `2026-08-23T19:20:18.813396Z`. Exact service build `a28aff1d` remained `Running / Live`, heartbeat advanced by one minute through `19:21:37.943992Z`, and `last_error` was null. PostgreSQL had zero waiting locks and zero active queries older than 30 seconds. Separately, one strategy has `live_stakes=true`; it produced seven Matched Live orders totaling `$42` during the last 24 hours, latest at `13:30:00.879624Z`, but none in the current five-minute cycle. No production state was changed.

## 2026-08-23T19:38:53.4601101Z
Request:
Проверь состояние этих стратегий и, если всё в порядке, сгенерируй их историю

Result:
Stopped before mutation because the exact production state is not ready. Read-only UTC previews proved that production `192.168.0.101:5432/polycopytrader` has 1,060 settled exact-parent runs but zero requested child strategies by ID/code/name, zero assignments, no LossDiff state/event tables, no migration `0002` ledger row, and still runs old build `a28aff1d`; local has the exact migration and both correctly configured zero states but zero settled parent history. A bounded raw-row causal replay independently cross-checked 22 potential Reset 4+ entries and 30 potential Positive 13+ entries, but generated no rows. Deployment/migration authority and the exact history artifact set are required before a production backfill contract and mutation.

## 2026-08-23T19:42:05.7841498Z
Request:
Сначала разбираемся, почему не применилась миграция

Result:
Read-only diagnosis proved migration `0002-eth-lossdiff-gated-children` did not fail; it was never available to the deployed service. Production is healthy on build `a28aff1d`, which is a strict Git ancestor of implementation commit `dfa82c91`; that deployed commit contains only migration `0001`, no LossDiff migration file, and no `0002` catalog registration. Service startup calls the schema initializer before the host, so the old catalog saw already-applied `0001` and had nothing pending. Production independently shows only ledger `0001`, zero LossDiff relations, and zero child strategies. The repository has no automatic deployment workflow: publish/install is manual. No database, service, deployment, strategy, or trading state was changed. The reason/actor for restarting the existing old artifact at `19:29:43Z` remains unknown without host Windows Service/file-log audit.

## 2026-08-23T19:45:55.9879290Z
Request:
Проверь сейчас

Result:
Production read-only recheck confirmed migration `0002-eth-lossdiff-gated-children` applied successfully at `2026-08-23T19:44:26.538951Z` with the exact expected checksum. Both tables, strategies, and exact parent assignments now exist; both children are enabled/unpaused Paper with `live_stakes=false`. New build `99f57d2d` is Running, heartbeat advanced through `19:45:27.134939Z`, and `last_error` is null. Both LossDiff values are zero under the common rollout cutoff, with zero imported events, child runs, or orders. One transient post-start waiting lock cleared by the second snapshot; final waiting-lock and blocked-session counts were zero. No production state was changed by the verification.

## 2026-08-23T20:03:12.5764448Z
Request:
Переходи

Result:
Completed only the approval-gated read-only design phase. Froze 1,060 exact pre-rollout parent FAK chains at cutoff `2026-08-23T19:44:26.488143Z` and source digest `sha256:c5f2baabe698c05eb0d8e4f6c98571390a07e858c1ac3c6a373bc7ead509bf01`. Independent causal/financial calculations agree on Reset 4+ = 22 trades, Net `41.43983784`, and Positive 13+ = 30 trades, Net `45.60525117`, all with Calculated fees. Only 10/22 Reset entries retain a modern embedded book snapshot versus 30/30 Positive; draft DEV-001 discloses parent-chain-only evidence for the other 12 and its ordinary Paper impact. Draft contract `RC-20260823-eth-lossdiff-history-backfill` validated at digest `sha256:40ece44da27b7059128ed4a7c6911316675d1acfc3a72268249de45bdc81101d`. No product or production mutation occurred; exact approval is required before implementation.

## 2026-08-23T20:50:22.8429271Z
Request:
`APPROVE RC-20260823-historical-parity-gross-ordered-strategy-completion sha256:ddae8fef59aed07fc98c719396f699370f6d385c377637a9d908a9342134bddf`

Result:
Implemented the approved pre-2026-08-10 historical Gross/Net workflow so one greatest-current-Gross unfinished strategy remains active through exact/local, donor/fixed, retry, and ordered Live initial-balance work before the remaining strategies are reranked. Donors remain unchanged; the terminal no-donor result is a new backward-compatible `Fixed0p033` decision with exact `Fee=ROUND_AWAY_8(B*0.033)` and `Net=Gross-Fee`, including when partial component evidence is recorded. All AccountingDecision evidence now includes strategy ID, observed rank/Gross, contract ID/digest, and calculation version. Existing completed results and the strictly newer valid VenueReported revision path remain unchanged. Isolated task-only verification passed 97/97 focused tests, 26 broader tests with 8 PostgreSQL skips due no configured test connection, solution build with 0 errors/126 warnings, and independent semantic review with no open findings. No deployment, database/service/configuration mutation, production operation, or trading action occurred.

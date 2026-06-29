## 2026-06-28T21:13:37.2331901Z
Request:
Мне казалось, что я уже давал такую задачу, но на всякий случай повторю - убедись в том, что все текущие стратегии делают ставки в FAK режиме со ставкой 0.99 для гарантированной выборки стека. Как в Paper, так и в Live режиме.

Result:
Forced current Up/Down FAK strategy order paths to use worst price 0.99 for Paper and Live while preserving actual fill VWAP separately; updated focused FAK Paper/Live tests. Focused verification passed. Full solution build was blocked by running Dashboard/Visual Studio locked DLLs.

## 2026-06-29T05:57:54.0433904+00:00
Request:
Почему не ставятся живые ставки в SOL Up or Down 5m Down 90 bps Reference Average Premarket ?

Result:
Queried production PostgreSQL `192.168.0.101` read-only for `sol_up_down_5m_down_bps_90_fak_premarket`. Confirmed the strategy is enabled and effective Live since `2026-06-29T05:34:47Z`, but the last qualifying Paper entry was created at `05:34:38Z`, 9 seconds before Live was enabled, so it stayed Paper-only. After Live enable there were `0` Paper orders, `0` paper-live shadow decisions, and `0` Live orders; later windows skipped with `preopen_entry_window_elapsed`, `entry_due_expired`, or `crypto_reference_fetch_failed`. No source code, DB writes, service restart, live order submission, or cancel action was performed.

## 2026-06-29T06:05:39.2465014+00:00
Request:
А разве при несоблюдении условий Live cond skip не должен увеличиваться?

Result:
Inspected Dashboard/PostgreSQL aggregation for `LiveConditionSkippedOrdersCount`. Confirmed `Live cond skip` increases for post-Live skipped runs with condition-like reasons such as `reference_average_move_below_bps_threshold`, but not for technical reasons. Production read-only classification for `sol_up_down_5m_down_bps_90_fak_premarket` currently shows `live_technical` skips only: `crypto_reference_fetch_failed=1`, `entry_due_expired=3`, `preopen_entry_window_elapsed=1`; no `live_condition` rows. No source code, DB writes, service restart, live order submission, or cancel action was performed.

## 2026-06-29T06:10:27.4582227+00:00
Request:
Объясни мне подробнее все возникшие technical skips

Result:
Rechecked production PostgreSQL `192.168.0.101` read-only and explained all technical skips for `sol_up_down_5m_down_bps_90_fak_premarket`: `entry_due_expired=4`, `preopen_entry_window_elapsed=1`, and `crypto_reference_fetch_failed=1`. `entry_due_expired` means due processing happened more than `EntryGraceSeconds=60` after `entry_due_at_utc`; for the `-30s` premarket strategy this means after `market_start + 30s`. `preopen_entry_window_elapsed` means processing happened after market start but before the expired threshold. `crypto_reference_fetch_failed` means the current SOL reference price was unavailable, so the 90 bps condition could not be evaluated. No source code, DB writes, service restart, live order submission, or cancel action was performed.

## 2026-06-29T06:19:06.4809137+00:00
Request:
А откуда берутся такие задержки, если у нас есть правило, что живые ставки отрабатывают в первую очередь?

Result:
Inspected the due-entry scheduling and ordering code for the Reference Average Premarket path. Confirmed that Live-first is a limited ordering rule: the processor runs the Live flow before the NonLive flow, and effective-Live rows are prioritized only within the selected due timestamp/batch. It does not create a dedicated realtime queue, preempt older due batches, or bypass cycle work such as observe/settlement processing, DB/API calls, bulk skip checks, Gamma market warmup, and per-cycle throttling. For the affected target timestamps there were `168` Reference Average runs per due timestamp and only `1` effective-Live target row; the target can be first inside its due batch but still be processed late if the worker reaches the PreOpen placement stage late. No source code, DB writes, service restart, live order submission, or cancel action was performed.

## 2026-06-29T06:31:15.7658210+00:00
Request:
Дай мне подробно таймстамп по обработке, я не понимаю, что именно вычисляется целую минуту

Result:
Queried production PostgreSQL read-only for target lifecycle timestamps, same-due entry-event ranks, API errors, and HTTP diagnostics. Confirmed target rows were created 8-9 minutes before due, so discovery was not late. Each target due timestamp had `361` `Crypto` due runs with only `1` effective-Live target run; the target planned due rank was `1`, but actual entry-event ranks were often last or near-last. For due `08:54:30+03`, early Diff rows processed at `08:54:37.098+03`, non-live Reference Average rows around `08:54:58.915+03`, the target Live row skipped `entry_due_expired` at `08:56:25.391+03`, and previous-score rows finished around `08:57:20.068+03`. Source inspection confirmed `entry_due_expired` is checked before Gamma lookup/strategy calculation, so the minute-scale delay is queue/cycle arrival delay, not a minute spent computing the Reference Average formula. No source code, DB writes, service restart, live order submission, or cancel action was performed.

## 2026-06-29T06:57:53+00:00
Request:
Давай добавляй. И я не понимаю, почему у нас до сих по последовательный Worker. Почему мы не можем каждую стратегию пускать в своём отдельном потоке?

Result:
Added production stage timing instrumentation for BTC/ETH/SOL Up/Down strategy processing. New table `btc_up_down_5m_strategy_stage_timings` stores cycle/stage timestamps, durations, counts, due ranges, success/error status, and flow kind for main, fast Diff, and previous-result workers. Instrumented due SQL, previous-result filtering, bulk middle-reference skip, market warmup, deferred persistence prepare/flush, decision tasks, observe, maker high-water, PreOpen sell exits, and settlement. Added repository/no-op/test support and README SQL for recent Europe/Sofia timelines. Explained that one raw thread per strategy would amplify CLOB/Gamma/DB/risk concurrency and race shared exposure/persistence; bounded fast lanes with shared caches should be based on the measured bottleneck. Verification: service project build passed; focused storage/no-op tests passed 2/2; broad test project compiled but still has known stale FAK/catalog failures from earlier work (694 passed, 57 failed). No live orders, production DB writes, service restart, or cancel action was performed.

## 2026-06-29T07:27:09.9162206+00:00
Request:
Выложил новую версию сервиса, проверяй. Наша конечная задача - задержка ни по одной из стратегий не должна превышать 3 секунд.

Result:
Checked the deployed service read-only in production PostgreSQL and confirmed it was running commit `936dd71` with stage timing diagnostics active, but the 3-second objective was not met: fresh batches had entry delays up to about 8.2 seconds. The new timings showed `fast_diff` due entries waiting behind long `observe_markets` cycles and main preopen entries waiting behind observe/maker plus deferred persistence prepare/flush. Added a main due-only fast lane, split Diff into observe-only and due-only processor methods/workers, switched the fast Diff worker to due-only processing, and guarded main due query/update paths with a semaphore to avoid duplicate run selection. Added a focused test proving fast Diff due no longer observes markets. Verification: solution build passed with existing nullable warnings; new focused test passed; existing snapshot Diff test passed; a stale existing FAK test still expects `0.45` although current FAK behavior uses worst price `0.99`. No production DB writes, live orders, service restart, or cancel action was performed.

## 2026-06-29T07:33:49.2066241+00:00
Request:
Continue working toward the active goal: довести задержку обработки каждой стратегии до не более 3 секунд и проверить текущую deployed версию сервиса.

Result:
Reinitialized repository context and retried production verification, but PostgreSQL at `192.168.0.101:5432` timed out from the local machine. Continued with the next measured bottleneck from prior stage timings: `deferred_persistence_prepare` was spending about 3 seconds loading paper positions before due decisions. Changed due-entry deferred persistence preparation to read paper positions from `ExposureSnapshotCache` instead of calling `GetPaperPositionsAsync` per FAK due batch, added `ExposureSnapshotCacheWarmupService` before strategy workers, and added focused tests proving fast Diff due uses the exposure cache and does not directly query repository paper positions. Verification: solution build passed with existing nullable warnings; focused fast Diff tests passed 2/2; snapshot Diff test passed 1/1; diff check passed with line-ending warnings only. No production DB writes, live orders, service restart, or cancel action was performed.

## 2026-06-29T07:39:29.6474822+00:00
Request:
Continue working toward the active goal: довести задержку обработки каждой стратегии до не более 3 секунд и проверить текущую deployed версию сервиса.

Result:
Rechecked local repo and again attempted production connectivity; `psql` and TCP checks to `192.168.0.101:5432` still timed out. Continued implementation against the remaining scheduler gap: previous-result strategies still used a full observe/due/settlement flow in their dedicated worker. Split previous-result processing into due-only (`ProcessPreviousResultFastDueEntriesAsync`) and observe-only (`ProcessPreviousResultObserveAsync`) methods while keeping `ProcessPreviousResultDueEntriesAsync` as a compatibility wrapper. Changed the existing previous-result worker to due-only fast polling, added and registered `BtcUpDown5mPreviousResultObserveWorker`, and added a focused test proving fast previous-result due does not observe markets. Verification: previous-result focused tests passed 3/3; solution build passed with existing nullable warnings; diff check passed with line-ending warnings only. No production DB writes, live orders, service restart, or cancel action was performed.

## 2026-06-29T07:47:01.8108552+00:00
Request:
Выложил новую версию сервиса, проверяй. Наша конечная задача - задержка ни по одной из стратегий не должна превышать 3 секунд.

Result:
Retried production verification from the local machine, but PostgreSQL at `192.168.0.101:5432` still timed out, including through the new gate command. Added `scripts/check-strategy-entry-latency.ps1`, a psql-based production latency gate that verifies the service heartbeat/version, checks enabled Up/Down 5m strategy entry delays against a configurable 3-second budget, prints worst over-limit rows, and summarizes BTC stage timings when available. Updated README with current split-lane `cycle_kind` values and the gate command. Verification: PowerShell syntax parse passed; local stale-DB smoke run completed with `-AllowNoRows`; production run failed with `psql ... timeout expired`; diff check passed with README line-ending warning only. No production DB writes, live orders, service restart, or cancel action was performed. Final objective remains open until fresh production data proves all strategy delays are <= 3 seconds.

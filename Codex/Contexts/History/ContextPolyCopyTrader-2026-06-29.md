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

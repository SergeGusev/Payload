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

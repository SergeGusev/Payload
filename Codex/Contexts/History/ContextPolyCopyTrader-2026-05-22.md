## 2026-05-22T04:29:30.3518283Z
Request:
Проверь свою статистику

Result:
Queried production PostgreSQL read-only at `192.168.0.101` without printing secrets. The service is alive in `Running`/`Live` mode with heartbeat age about `45s`, but it is still on build `info=1.0.0+aad193e9a0279d804a265a7c3c79a88c1f1d9d20`, not the new scanner commit `4c248a2`. The new `btc_up_down_5m_arbitrage_scans` table does not exist in production yet, so the covered-arbitrage scanner has not been deployed/restarted there. Existing `btc_up_down_5m_statistics_ticks` has `175660` rows, but latest sample is `2026-05-19T05:18:12Z`; there were `0` ticks in the last hour/15 minutes. No scanner/statistics API errors appeared in the last hour; only stale Binance ETH/USDT errors from `BtcUpDown5mPaperStrategyProcessor/GetCryptoReferencePrice`. Temporary C# probe was removed. No source code change, DB write, service restart, live order submission, cancel action, or tests were performed.
## 2026-05-22T05:13:52.5797634+00:00
Request:
Верни автоматические паузы. Принцип такой: 1. При каждом поражении делаем выборку по данной стратегии за 12 часов. 2. Если сделано больше одной ставки и выручка со стратегии отрицательная, она уходит в паузу на 12 часов и не делает ставок. 3. Возможность управлять паузами вручную, через галочку - оставь.

Result:
Restored automatic strategy pausing after losing settlements. The service now checks the same strategy's 12-hour settled Paper/Live history, pauses for 12 hours only when settled count is greater than 1 and realized PnL is negative, preserves manual indefinite pauses from the Dashboard checkbox, and no longer clears timed pauses on schema initialization. Updated docs/tests; focused tests passed 163/163, full solution tests passed 528/528, and `git diff --check` passed with LF/CRLF warnings only.

# Context History — 2026-08-25

## Production server and betting check

Read-only UTC snapshots of exact PostgreSQL 18.3 primary `192.168.0.101:5432/polycopytrader` confirmed service build `56294c1fe75838dfc83afecb41dcf4b91fbcb73b` is `Running / Live`, heartbeat advances, `last_error=NULL`, BTC/ETH/SOL references are fresh, and Paper betting continues at high volume. Initial 5/15/60-minute Paper counts were 107/392/1,919; independent run counts and later new-order cycles confirmed continued entry and settlement processing. The sole Live-enabled strategy had no Live order in the last hour, 16 Matched and two Cancelled zero-fill intents in 24 hours, and no open Live order.

One transient database event produced 17 lock waiters, all gone within about 23 seconds; the exact blocker was not captured. A verified ongoing degradation affects `PaperCopiedTraderPerformanceWorker`: repeated `Exception while reading from stream` failures occur about every 30 seconds, the 5,504-row copied-trader projection stopped refreshing at `2026-08-25T05:55:55.011990Z`, queue/inflight counts were 496/25, and live `pg_stat_activity` repeatedly showed its `WITH event_rows AS (...)` aggregate running 17-26 seconds without a lock wait before another failure. The stored error lacks the inner exception, so the terminal cause remains unknown. Twelve critical crypto Polymarket WebSocket receive-loop abort/reconnect errors ended at `06:24:34Z`; no later error was present, while betting and references remained fresh, but exact socket recovery was not independently persisted. No production or source state was changed.

## 2026-08-25T07:21:53.7188759Z
Request:
Ок, доработай

Result:
Read-only production evidence found 25 durable inflight wallets covering 176,555 orders, 176,555 fills, 25 open positions, and 176,480 settlements; the aggregate was cancelled at approximately 30 seconds and retried the same work. After exact user approval of `RC-20260825-copied-performance-aggregate-timeout` at `sha256:aa5e4eb1b6b376e35983cdc4145a7996e2c5a8d22478e0b5a62b7972242258d6`, added a dedicated 180-second timeout only to the existing `WITH event_rows` command. SQL, calculations, batching, cadence, transactions, recovery, retries, and betting behavior are unchanged. Focused tests passed 1/1 and 20/20; 14 database integration cases returned early without the absent test connection, Release solution build passed with zero errors and 126 pre-existing warnings, and independent semantic review returned PASS. No production state was changed; deployment and runtime verification remain with the user.

## 2026-08-25T19:21:01.3401170Z
Request:
Выложил, проверяй

Result:
Read-only production verification accepted deployed build `3023d6c46d176eef579734a81bac2fd1e5ba4824`, which contains timeout implementation `62ea04d9`. Service remained `Running / Live` with advancing heartbeat and no heartbeat error. Copied-performance projection advanced through four observed timestamps; an aggregate observed from `19:18:02.531445Z` completed at `19:18:54.778241Z`, beyond the former approximately 30-second failure boundary. Queue/inflight work progressed, no post-start projection error occurred, and zero lock waiters were present. Betting crossed a new five-minute boundary with 66 new Filled Paper orders and matching Entered runs; BTC/ETH/SOL references were fresh. One startup BTC no-price warning recovered. No production state changed. Deployed local commit `3023d6c4` remains one commit ahead of upstream and was not pushed by this read-only check.

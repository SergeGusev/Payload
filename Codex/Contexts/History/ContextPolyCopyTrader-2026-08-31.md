# PolyCopyTrader History — 2026-08-31

## Production Health, Betting, And Logs

- Verified Production read-only at database cutoff `2026-08-31T06:20:01.966471Z`: service `Running`/`Live`, exact build `6171b515793fabe216761474e82fa27312020e4b`, unchanged start `2026-08-30T10:39:22.860194Z`, heartbeat age `26.202s`, NULL `last_error`, and zero blocked PostgreSQL sessions.
- Confirmed active Paper betting with 866 new orders and 866 fills in the bounded preceding-15-minute sample, all orders Filled. An independent latest-row check showed three matching order/fill pairs at `2026-08-31T06:19:31.069Z`.
- Confirmed fresh persisted BTC/ETH/SOL Binance reference ticks at `2026-08-31T06:21:46.695409Z`, with sampled ages about `5.35s`.
- Server logs from `08:14:00` through `09:19:01.693` Europe/Sofia contained 24 recurring Legacy historical-accounting backfill failures; the latest was an Npgsql read timeout in `ApplyHistoricalPaperFakFeeBackfillBatchAsync`.
- The same interval contained 1,043 slow market-data side-effect warnings. `TryUpdatePaperPositionMarks` remained the verified slow processing operation, reaching `4,676.6721ms`; maximum queue delay was `10,537.3706ms` and observed pending depth reached 115.
- Despite those spikes, queue metrics showed continued throughput and recovery: 763,768 additional processed updates, zero rejected/failed updates, final depth three pending plus one in flight. Soft-limit overflows increased by 83.
- Also observed transient WebSocket reconnects, two stale ETH reference-tick skips followed by persisted recovery, and intermittent OKX expiry-futures two-second HTTP timeouts. OKX recovery was not independently established from persisted state.
- No Production or product-source state was changed. One broad read-only aggregate hit the required 15-second timeout; subsequent verification used indexed bounded samples.

## 2026-08-31T06:47:25.4108788+00:00
Request:
Ок, делай необходимые доработки

Result:
Locked the exact local-edit scope to an indexed Legacy apply correction and stage-only Paper mark persistence telemetry. Drafted and validated requirement contract `RC-20260831-legacy-apply-index-mark-stage-telemetry` with semantic digest `sha256:a8b77cefa2e0236e740cec112d10176487548170959a149159569c78e44d6f60`; product edits are blocked pending the user's exact later approval. No Production or product source state was changed.

## 2026-08-31T06:50:06.4061510+00:00
Request:
`APPROVE RC-20260831-legacy-apply-index-mark-stage-telemetry sha256:a8b77cefa2e0236e740cec112d10176487548170959a149159569c78e44d6f60`

Result:
Recorded the user's exact approval against the unchanged semantic digest, including both disclosed deviations. The approved contract is ready for its mandatory pre-implementation Git checkpoint; no product source or Production state has yet changed.

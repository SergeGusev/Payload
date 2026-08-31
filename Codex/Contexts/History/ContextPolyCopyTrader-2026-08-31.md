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

## 2026-08-31T07:26:07.1573683+00:00
Request:
`APPROVE RC-20260831-legacy-apply-index-mark-stage-telemetry sha256:a8b77cefa2e0236e740cec112d10176487548170959a149159569c78e44d6f60`

Result:
Completed the approved bounded local correction. Added ordered non-transactional migration `0006` with the exact concurrent partial expression index and fail-closed catalog check; split only the Legacy apply parity-audit exclusion into equivalent source-specific indexed predicates; and added telemetry-only Paper mark stages through `ApplyExposureCache` without changing mark persistence, queue or trading behavior. Real PostgreSQL Release tests passed 1/1, 5/5 and 4/4; final focused tests passed 65/65; Release solution build passed with 0 errors and 126 existing warnings. Independent reviewer `agent:/root/reviewer_legacy_apply_mark_stages` returned PASS with no findings. The exact disposable database and the protected 304,392,941-byte marked temp run were removed and independently verified absent. No Production or deployment state was changed; the user controls deployment.

## 2026-08-31T07:51:47.9846800+00:00
Request:
Выложил, проверяй

Result:
Verified Production read-only after deployment of exact commit `e5335ec3263032fe4e4e188c8261fd3549f26aec`. Service was Running/Live with fresh heartbeat and NULL last error; migration `0006` and its exact valid/ready/live index were confirmed. The process produced 603 Filled Paper orders with 603 matching fills and updated 863 Paper positions in the bounded post-start sample. The Legacy worker completed 15 cycles with zero failures, Error events, or query cancellations; one requested candidate was a structural conflict and no eligible apply row had yet exercised a larger update page. Logs contained zero ERR/FTL, Legacy cycle failures, or slow side-effect warnings; queue boundary samples were empty with zero rejected/failed/overflow work. Two transient BTC/SOL reference warnings recovered, independently confirmed by fresh persisted BTC/ETH/SOL ticks. No Production state was changed.

## 2026-08-31T19:20:16.2511490+00:00
Request:
Проверь сервер ставки и логи

Result:
Verified Production read-only through database cutoff `2026-08-31T19:20:16.251149Z` and server-log cutoff `2026-08-31 22:21:47.545+03:00`. Service remained Running/Live on exact build `e5335ec3263032fe4e4e188c8261fd3549f26aec` with unchanged start, fresh heartbeat, NULL last error, and zero waiting locks. Paper execution was active: the latest 1000-order sample was entirely Filled, a later bounded sample contained 994 Filled and six lawful Maker-GTD expirations, and the final latest ten were all backed by fill rows. The six Maker expirations carry the mandatory `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills` classification. Longer-run evidence superseded the earlier short clean Legacy sample: the latest 1000 backfill events contained 22 cycle failures, with the last at `18:24:01.096839Z` independently traced in logs to a candidate-query Npgsql read timeout. The worker subsequently completed 30 cycles and applied 880 fills, 882 runs, 880 positions, and 880 settlements, with no later failure. Current logs showed transient general side-effect and Maker-evidence latency up to 6.63 seconds queue delay and 17.46 seconds processing respectively, but final queue metrics recovered to six general pending and zero Maker pending with zero rejected/failed work. A WebSocket disconnect and a stale SOL tick both recovered. No Production, database, service, configuration, strategy, order, deployment, or product-source state was changed.

## 2026-08-31T20:04:33.3147852+00:00
Request:
Ок, займись дальнейшей доработкой

Result:
Locked the local-edit scope to the two residual Production findings without changing Production. Exact inspection traced the Legacy candidate failure to an unbounded order-id array passed to the legacy PaperRun parity command; current rows no longer preserve the failed snapshot's exact array size. General latency remains attributable to Paper position-mark SQL execution, while the internal phase of the 17.46-second dedicated Maker-GTD stall remains unknown. Drafted and validated contract `RC-20260831-legacy-candidate-and-market-data-latency`: bounded 500-ID parity chunks with exact candidate semantics, backlog-only coalescing of intermediate general position-mark persistence while every order/evidence event remains processed, and telemetry-only Maker-GTD phase attribution. The two behavior/coverage limits are disclosed explicitly. Semantic digest: `sha256:2a4d373da57c60a3be1f4f5ac573f57b40d1f693137b0ab353eead2ea4f2b813`. Product edits are blocked pending exact user approval.

## 2026-08-31T20:42:25.7635804+00:00
Request:
`APPROVE RC-20260831-legacy-candidate-and-market-data-latency sha256:2a4d373da57c60a3be1f4f5ac573f57b40d1f693137b0ab353eead2ea4f2b813`

Result:
Recorded the user's exact approval against the unchanged semantic digest, including both disclosed deviations: backlog-coalesced intermediate unrealized position marks and telemetry-only Maker-GTD diagnosis in this change. The approved contract is ready for the mandatory separate pre-implementation Git checkpoint; no product source or Production state has changed.

## Active Update 2026-05-02 Concrete Wallet Selection Clarification
Goal: Clarify whether concrete wallets to follow are already selected.
Status: Completed
Done:
- Checked current code/docs references for watchlist, tracked wallets, candidate tables, and wallet/category performance.
- Clarified that the on-chain candidate pipeline does not yet maintain a separate explicit list of target/tracked wallets.
- Clarified that current candidate preparation evaluates all wallets present in downloaded on-chain wallet fills and filters per concrete trade event using wallet/category performance.
- Clarified that existing `Watchlist` belongs to the older API scanner path and is not the selector for the new on-chain candidate pipeline.
Next: Add the next decision/targeting layer: either derive eligible wallet/category targets from `polymarket_onchain_wallet_category_performance` and `Accepted` candidates, or create an explicit reviewed target-wallet/category table with manual overrides.
Notes: Explanation only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Candidate Entity Clarification
Goal: Clarify whether a signal candidate represents a wallet or a trade event.
Status: Completed
Done:
- Clarified that `polymarket_onchain_signal_candidates` is event-level, keyed by `source_fill_id + participant_role`, not a wallet leaderboard/profile table.
- Clarified that the candidate row already contains the wallet, counterparty, side, token, market/category metadata, leader wallet/category performance snapshot, and candidate decision fields.
- Clarified that the next decision layer will select concrete candidate events from concrete wallets, not discover/select the wallet after the candidate is created.
Next: Use `Accepted` candidate rows as potential copy-signal events, then apply freshness, order-book, spread, liquidity, risk, and deduplication gates.
Notes: Explanation only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Candidate Selection Logic Explanation
Goal: Explain the current signal-candidate acceptance/rejection logic and update flow.
Status: Completed
Done:
- Rechecked `OnChainSignalCandidateProcessor`, `OnChainSignalCandidateWorker`, PostgreSQL queue/cursor SQL, schema, and config defaults.
- Clarified that candidate selection currently accepts only BUY wallet-fill sides with sufficient notional, known/enriched active unresolved market metadata, known category, and fresh wallet/category performance passing configured sample, score, ROI, and win-rate thresholds.
- Clarified that rejected candidates persist one or more explicit reason rows and use the first reason as `decision_code`.
- Clarified that new data is queued at wallet-fill upsert time, historical data is seeded through `polymarket_onchain_signal_candidate_backfill_cursors`, temporary metadata/category/performance rejections are retried, and processed rows are deleted from the refresh queue after candidate/reason upsert.
- Clarified that spread/liquidity/current order book/portfolio risk checks are intentionally not part of the candidate-preparation layer and belong to the next decision/paper-signal layer.
Next: Implement the decision layer that reads `Accepted` candidates and applies freshness, order-book, spread, liquidity, portfolio/risk, and deduplication gates before creating paper signals.
Notes: Explanation only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Signal Candidate Completion Answer
Goal: Clarify whether the signal-candidate preparation functionality is complete.
Status: Completed
Done:
- Rechecked the implemented signal-candidate pipeline: schema tables, queue/cursor, worker, processor, source selection, upsert, rejection reasons, config, and docs exist.
- Confirmed the pipeline is a completed read-only preparation layer: it materializes candidate rows and explicit rejection reasons from on-chain wallet fills, joined with token metadata and wallet/category performance.
- Confirmed the live database is still backfilling: about 28,230 candidate rows existed during the check, `polymarket_onchain_signal_candidate_refresh_queue` had about 1,670,222 rows, and backfill cursor `default` had `completed=false`.
- Confirmed recent signal-candidate worker errors were absent in the last 30 minutes during the check.
Next: Treat candidate preparation code as complete, but keep monitoring queue drain/cursor completion before considering the historical candidate dataset complete.
Notes: Operational answer only; no source behavior changed. A temporary C# diagnostic project under `Codex/TempDbCheck` was created and removed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Startup Index Lock Unblock
Goal: Diagnose and prevent a startup hang on an existing PostgreSQL index.
Status: Completed
Done:
- Inspected the clipboard screenshot and identified startup statement `117/162` as `CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_refresh_queue_queued`.
- Checked PostgreSQL with `POLYCOPYTRADER_POSTGRES_CONNECTION`; the target index already existed, was valid, and was about 2 MB, so no real index build was in progress.
- Found the startup `CREATE INDEX IF NOT EXISTS` backend waiting on a relation lock held by an old long-running `INSERT ... missing_activity` seed into `polymarket_onchain_wallet_activity_refresh_queue`.
- Canceled blocker backend `60728` with `pg_cancel_backend`; confirmed no active `CREATE INDEX`, no index progress rows, no blocking chain, fresh service heartbeat, and no recent `api_errors`.
- Updated `PostgresSchemaInitializer` to pre-check catalog existence for `CREATE INDEX IF NOT EXISTS` statements and skip already-existing indexes before sending the SQL to PostgreSQL, avoiding no-op startup lock waits.
- Added unit coverage for parsing `CREATE INDEX IF NOT EXISTS` and `CREATE UNIQUE INDEX IF NOT EXISTS` statements.
Next: Rebuild/restart the service when convenient so the startup no-op index skip is present in the Debug/Service binary; the currently running service is already unblocked and healthy.
Notes: Verification passed: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "StorageTests"` 10/10; first parallel service build hit a transient Microsoft Defender lock, then `dotnet build-server shutdown` and `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 146/146; `git diff --check` passed with line-ending warnings only. A temporary C# diagnostic project under `Codex/TempDbCheck` was created and removed. Existing unrelated dirty `PolyCopyTrader.sln` and preexisting formatting/console-output edits in `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left unstaged. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Signal Candidate Backfill Restart Check
Goal: Verify the restarted full-history signal-candidate worker and fix the first live bottleneck.
Status: Completed
Done:
- Confirmed `PolyCopyTrader.Service` restarted in `ReadOnly` as process `57900`, with fresh heartbeats after schema initialization.
- Confirmed new signal-candidate queue/cursor tables exist and backfill started; cursor row `default` is advancing.
- Confirmed `ix_polymarket_onchain_wallet_fills_signal_candidate_backfill` exists and was created at about 1.8 GB.
- Found `OnChainSignalCandidateWorker` initially timed out in `GetPolymarketOnChainSignalCandidateSourcesAsync` because queue rows join `polymarket_onchain_wallet_fills` by `(source_fill_id, role)` without an index.
- Added schema index `ix_polymarket_onchain_wallet_fills_source_role` and created it live with `CREATE INDEX CONCURRENTLY`; live index build took about 581 seconds and size is about 1.4 GB.
- Confirmed the worker recovered after the index: recent log cycles completed with `SourcesQueued=1000`, `SourcesFetched=250`, `CandidatesUpserted=250`, and candidate rows increased from 0 to 750 by the final check.
- Confirmed no PostgreSQL blocking chain during checks; old API errors remain from before the index was available.
Next: Leave the service running; watch `polymarket_onchain_signal_candidates` and `polymarket_onchain_signal_candidate_refresh_queue` trend. Queue can grow while historical seeding runs faster than processing, then should drain after backfill cursor completes.
Notes: Verification passed after the code fix: targeted `StorageTests|OnChainSignalCandidateTests` 10/10; service build passed after `dotnet build-server shutdown`; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` 142/142; `git diff --check` passed with line-ending warnings only. A temporary C# diagnostic project under `Codex/TempDbCheck` was created and removed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Full Signal Candidate Backfill
Goal: Replace the recent-only candidate worker with a full downloaded-history backfill plus continuous tail processing.
Status: Completed
Done:
- Replaced `SignalCandidateLookbackHours` with queue-oriented config: `SignalCandidateQueueSeedBatchSize` and `SignalCandidateRetryBatchSize`.
- Added PostgreSQL tables `polymarket_onchain_signal_candidate_refresh_queue` and `polymarket_onchain_signal_candidate_backfill_cursors`.
- Added index `ix_polymarket_onchain_wallet_fills_signal_candidate_backfill` for cursor-ordered historical candidate source traversal.
- Changed ingestion-derived wallet-fill upserts to enqueue new source rows for candidate materialization.
- Changed `OnChainSignalCandidateWorker` flow: seed historical queue by cursor, enqueue refreshable temporary rejections, process queued sources, upsert candidate/reason decisions, then remove processed queue rows.
- Updated README, configuration reference, project memory, appsettings, storage contracts, no-op/test repositories, and signal-candidate tests.
Next: Restart the service so schema initialization creates the new queue/cursor tables and the worker starts backfilling `polymarket_onchain_signal_candidates` across the full downloaded wallet-fill dataset.
Notes: Verification passed: targeted `OnChainSignalCandidateTests|StorageTests|ConfigurationTests` 19/19; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` 142/142; service build passed; dashboard build passed; `--print-config` passed; `git diff --check` passed with line-ending warnings only. Parallel test/build attempts again hit transient `obj` file locks; after `dotnet build-server shutdown`, sequential checks passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Signal Candidate Scope Clarification
Goal: Clarify the current signal-candidate worker scope and the mismatch with the intended full downloaded dataset.
Status: Completed
Done:
- Confirmed the current implementation limits candidate source selection with `SignalCandidateLookbackHours = 24`.
- Confirmed the repository SQL filters `polymarket_onchain_wallet_fills` by `wallet_fill.block_timestamp_utc >= now() - (@LookbackHours * interval '1 hour')`.
- Clarified that the 24-hour window was a conservative load guard for the first implementation, not a product requirement.
- Identified that this does not match the intended target: candidates should cover the whole currently downloaded/derived on-chain dataset, then continue processing new incoming rows.
Next: Replace the rolling-only lookback with a bounded historical candidate backfill plus continuous tail processing, ideally using a cursor/queue so each cycle processes a batch without rescanning the whole dataset.
Notes: Explanation only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Signal Candidate Restart Health Check
Goal: Verify PostgreSQL/service health after starting the service with the on-chain signal-candidate pipeline.
Status: Completed
Done:
- Confirmed `PolyCopyTrader.Service` is running in `ReadOnly` as process `57112`, with fresh heartbeats during checks.
- Confirmed `polymarket_onchain_signal_candidates` and `polymarket_onchain_signal_candidate_reasons` exist in PostgreSQL.
- Confirmed `OnChainSignalCandidateWorker` started and runs every minute; current cycles complete with `SourcesFetched=0`.
- Confirmed `SourcesFetched=0` is expected right now because `polymarket_onchain_wallet_fills` currently ends at `2026-04-26T09:18:26Z`, about 144 hours behind, while signal-candidate lookback is 24 hours.
- Confirmed ingestion is progressing again: `polymarket_onchain_ingest_cursors` for `CTF Exchange V1` advanced to block `86037531`, completed at `2026-05-02T09:16:36Z`.
- Confirmed no PostgreSQL blocking chain and no active sessions older than 5 minutes during the final check.
- Noted recent transient errors: 2 `OnChainIngestionWorker` Polygon RPC connection failures and 2 `OnChainMarketEnrichmentWorker` HTTP timeouts in the last 30 minutes.
Next: Let ingestion continue until `polymarket_onchain_wallet_fills.max(block_timestamp_utc)` reaches the last 24 hours; then `polymarket_onchain_signal_candidates` should start receiving rows. If cursor progress remains slow, optimize the block-range wallet execution/position rebuild path.
Notes: Operational check only; no application source behavior changed. A temporary C# diagnostic project under `Codex/TempDbCheck` was created and removed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 On-Chain Signal Candidate Pipeline
Goal: Add a read-only decision-prep layer that turns on-chain wallet fills into scored copy-signal candidates with explicit rejection reasons.
Status: Completed
Done:
- Added domain models, repository methods, PostgreSQL tables, and indexes for `polymarket_onchain_signal_candidates` and `polymarket_onchain_signal_candidate_reasons`.
- Added `OnChainSignalCandidateWorker` and `OnChainSignalCandidateProcessor`; the worker reads recent `polymarket_onchain_wallet_fills`, joins token metadata and wallet/category performance, then upserts accepted/rejected candidate decisions.
- Candidate acceptance currently requires a buy fill, minimum leader notional, known market category, usable market metadata, active/unresolved market state, fresh leader category performance, and the configured leader performance thresholds.
- Rejections are persisted with explicit reason codes, including sell side, small notional, missing category, missing/stale leader category performance, inactive/resolved market, and missing metadata.
- Added configuration defaults and validation for background signal-candidate refresh: enabled by default, interval `60s`, batch `250`, lookback `24h`.
- Updated README, configuration reference, project memory, and tests.
Next: Restart the service so schema initialization creates the new tables and the background worker starts filling them; then inspect candidate status/reason counts in PostgreSQL.
Notes: Verification passed: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` 141/141; service build passed; dashboard build passed; `--print-config` passed and includes the new signal-candidate refresh setting; `git diff --check` passed with line-ending warnings only. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Next Step Planning
Goal: Define the next implementation direction while PostgreSQL slow-query logs accumulate.
Status: Completed
Done:
- Reviewed the current persisted state: on-chain derived tables exist, refresh-worker contention was reduced, and PostgreSQL slow-query plan logging is enabled in `D:\PortgreeLogs`.
- Recommended the next work sequence: stabilize/monitor data pipelines, verify category/data-quality coverage, then implement the decision layer that converts leader fills into scored copy-signal candidates with explicit rejection reasons.
- Kept live trading out of scope; the next functional layer should remain `ReadOnly`/`Paper`.
Next: Implement the signal-candidate/rejection pipeline on top of `polymarket_onchain_fills`, `polymarket_onchain_wallet_category_performance`, market metadata, and configurable freshness/spread/liquidity/risk filters.
Notes: Planning only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 PostgreSQL Slow Query Plan Logging
Goal: Enable PostgreSQL slow-query logging with execution plans in `D:\PortgreeLogs`.
Status: Completed
Done:
- Confirmed PostgreSQL service is `postgresql-x64-17`, running from `D:\PortgreeData` as `NT AUTHORITY\NetworkService`.
- Confirmed `D:\PortgreeLogs` exists and granted explicit modify rights to the NetworkService SID.
- Confirmed the DB connection user is PostgreSQL superuser `postgres`.
- Found `shared_preload_libraries=auto_explain` and `logging_collector=on` were already active, but `auto_explain.log_min_duration` was `0ms`, causing every plan to be logged.
- Changed PostgreSQL settings with `ALTER SYSTEM` and `pg_reload_conf()` so logs now go to `D:/PortgreeLogs` and only statements/plans slower than `2000ms` are logged.
- Enabled useful diagnostics: `auto_explain.log_analyze=on`, `auto_explain.log_buffers=on`, `auto_explain.log_format=json`, `auto_explain.log_nested_statements=on`, `auto_explain.log_timing=off`, `log_lock_waits=on`, `log_temp_files=64MB`, `track_io_timing=on`, `log_rotation_size=100MB`.
- Disabled broad statement spam with `log_statement=none` and `log_duration=off`.
- Verified with a `pg_sleep(2.2)` probe that `D:\PortgreeLogs\postgresql-2026-05-02_111921.log` receives both duration lines and JSON execution plans.
Next: Let the service run under load, then analyze `D:\PortgreeLogs` for repeated slow plans and add targeted indexes.
Notes: PostgreSQL server configuration only; no source code behavior changed. No PostgreSQL restart was needed because `auto_explain` and `logging_collector` were already active and all checked settings had `pending_restart=False`. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Post Restart Contention Health Check
Goal: Verify service/database health after restarting with the reduced refresh-worker contention changes.
Status: Completed
Done:
- Confirmed `PolyCopyTrader.Service` is running as process `72708`, started at `2026-05-02 11:06:54 +03`.
- Confirmed the service loaded the new refresh settings at `2026-05-02 11:07:01 +03`: position `60s/25/100`, activity `90s/50/100`, performance `120s/50/100`, category performance `150s/250/250`.
- Checked PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed heartbeat is fresh: `PolyCopyTrader.Service` is `Running` in `ReadOnly`, heartbeat age about 2 seconds during the check.
- Confirmed `blocked_count=0`, no active sessions older than 5 minutes, and no `api_errors` since the restart.
- Confirmed one derived refresh session can hold the advisory lock while other derived refresh cycles skip with `0` processed/upserted rows; later cycles processed activity, position, and category-performance batches without deadlocks in the checked window.
- Queue counts during the check were approximately: token metadata `20`, position `42,954`, activity `143,119`, performance `86,009`, category performance `79,827`.
Next: Let the service run for at least 15-30 minutes, then recheck `api_errors`, active sessions, and queue trend; current post-restart state looks healthy.
Notes: Operational DB/log check only; no source behavior changed. Exact `count(*)` on the largest on-chain tables was avoided after timeout and replaced with PostgreSQL relation estimates to reduce load. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Reduce Refresh Worker Contention
Goal: Reduce database contention and deadlocks among on-chain derived refresh workers.
Status: Completed
Done:
- Added a shared non-blocking PostgreSQL transaction advisory lock to activity, position, wallet-performance, and wallet/category-performance refresh cycles in `PostgresAppRepository`.
- Workers that cannot acquire the derived-refresh lock now skip their current repository cycle instead of overlapping heavy transactions against the same materialized tables.
- Changed the initial `missing_activity` seed to read distinct wallets from `polymarket_onchain_wallet_fills` instead of the heavier `polymarket_onchain_wallet_executions` aggregate table.
- Changed position-triggered performance and category-performance queue inserts to `ON CONFLICT DO NOTHING`, avoiding updates to already-queued rows.
- Staggered/lowered derived refresh defaults: position `60s/25/100`, activity `90s/50/100`, performance `120s/50/100`, category performance `150s/250/250`.
- Updated service/dashboard appsettings, domain defaults, README, and configuration reference.
Next: Restart the service to load the new code/config, then monitor `api_errors` and queue counts; expected effect is fewer deadlocks/timeouts at the cost of more serialized refresh cycles.
Notes: Verification passed: targeted `StorageTests|OnChainIngestionTests|ConfigurationTests` 27/27; full test suite 138/138; service build passed; dashboard build passed; `git diff --check` passed. The first parallel test/dashboard build hit transient `VBCSCompiler`/Defender file locks; after `dotnet build-server shutdown`, sequential runs passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-02 Overnight Monitor Log Review
Goal: Review the overnight health monitor log and current database state.
Status: Completed
Done:
- Reviewed `D:\1\polycopy-overnight-health-20260501-235733.log`; monitor process `pid=67592` is still running.
- The log currently contains checks `1/20` through `15/20`; all checks are `WARN`, with no monitor `ERROR` rows.
- Across the log, service heartbeat stayed fresh and `PolyCopyTrader.Service` remained `Running` in `ReadOnly`.
- Across the log, `blocked=0`, `schema_active=0`, and the wallet-activity queue index existed, so the prior startup/index lock did not recur.
- Current live DB check also showed `blocked=0`, heartbeat age about 8 seconds, and no long active queries over 10 minutes.
- Repeated warnings are from refresh-worker failures: `OnChainActivityRefreshWorker` stream timeouts, `OnChainPositionRefreshWorker` deadlocks, some `OnChainPerformanceRefreshWorker` deadlocks, plus one market-enrichment HTTP timeout and one Polygon RPC HTTP 400.
- Queue trend: token metadata queue dropped from about 19,874 to 22, while position/activity/performance/category-performance refresh queues grew.
Next: Leave the monitor running until it finishes, but make the next implementation task reducing refresh-worker contention and the heavy wallet-activity seed path.
Notes: Log/DB inspection only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Overnight Health Monitor
Goal: Set up overnight health checks while the service continues running.
Status: Completed
Done:
- Created and started a temporary .NET overnight monitor outside the repository as hidden process `pid=67592`.
- Monitor log path: `D:\1\polycopy-overnight-health-20260501-235733.log`.
- Monitor will run 20 checks at 30-minute intervals, covering about 10 hours.
- Stopped an earlier monitor process after detecting false positives in its SQL self-matching, then launched the corrected version.
- First corrected check: service heartbeat fresh, `PolyCopyTrader.Service` `Running` in `ReadOnly`, `blocked=0`, wallet-activity queue index exists, no active schema index creation remains.
- First corrected check was `WARN` because of 2 recent `OnChainActivityRefreshWorker` stream timeouts and one long autovacuum on `polymarket_onchain_trade_details`; neither was blocking.
Next: Review `D:\1\polycopy-overnight-health-20260501-235733.log` in the morning; if repeated `WARN` rows show activity-refresh stream timeouts or long `missing_activity` seed queries, optimize that wallet-activity queue seed.
Notes: Operational monitor setup only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post Blocker Cancellation Health Check
Goal: Verify service and PostgreSQL health after cancelling the wallet-activity queue blocker.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed no blocking chain remains: `blocked=0`.
- Confirmed `ix_polymarket_onchain_wallet_activity_refresh_queue_queued` exists and no active `CREATE INDEX` session remains for it.
- Confirmed service heartbeat is fresh: `PolyCopyTrader.Service` is `Running` in `ReadOnly`, heartbeat age about 6 seconds.
- Confirmed no `api_errors` rows appeared in the last 15 minutes in the health query.
- Noted one active `missing_activity` seed query (`pid=58160`) still running for about 5 minutes, but it is not blocking anything.
Next: Treat the system as healthy now, but optimize the full wallet-activity `SELECT DISTINCT` seed if it keeps running for many minutes or recurs on startup.
Notes: DB health check only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Cancel Wallet Activity Queue Blocker
Goal: Cancel the PostgreSQL query blocking service schema startup.
Status: Completed
Done:
- Rechecked the blocker before cancellation: `pid=56984` was still running the expected `INSERT INTO polymarket_onchain_wallet_activity_refresh_queue ... SELECT DISTINCT execution.wallet FROM polymarket_onchain_wallet_executions ...`.
- Ran `pg_cancel_backend(56984)` through `POLYCOPYTRADER_POSTGRES_CONNECTION`; PostgreSQL returned `True`.
- Confirmed the previously blocked `CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_refresh_queue_queued` session disappeared after cancellation.
- Confirmed there were no remaining blocking rows for that schema index; a new `missing_activity` seed query appeared as `pid=58160`, but it was not blocking schema index creation.
- Confirmed service heartbeat is fresh: `PolyCopyTrader.Service` is `Running` in `ReadOnly`, heartbeat age about 4 seconds at verification time.
Next: Watch whether the new `missing_activity` seed query times out or keeps recurring; if it does, replace that full `SELECT DISTINCT` scan over `polymarket_onchain_wallet_executions` with a lighter/batched queue seeding path.
Notes: Operational DB cancellation only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Wallet Activity Queue Index Lock Diagnosis
Goal: Inspect the clipboard screenshot and determine whether the long startup pause is expected.
Status: Completed
Done:
- Extracted and inspected the Windows clipboard screenshot; service startup is stuck at PostgreSQL schema statement `114/149`, `CREATE INDEX IF NOT EXISTS ix_polymarket_onchain_wallet_activity_refresh_queue_queued`.
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Found the index creation session `pid=15828` active for about 29 minutes and waiting on `Lock:relation`.
- Found blocker session `pid=56984` active for about 34 minutes running `INSERT INTO polymarket_onchain_wallet_activity_refresh_queue ... SELECT DISTINCT execution.wallet FROM polymarket_onchain_wallet_executions ...`.
- Confirmed `polymarket_onchain_wallet_activity_refresh_queue` is not huge for this symptom: about 27 MB, estimated/live rows around 118k/117k.
Next: Cancel blocker `pid=56984` with `SELECT pg_cancel_backend(56984);` to unblock startup; if this recurs, optimize/disable the full missing-activity startup seed from `polymarket_onchain_wallet_executions`.
Notes: DB/image diagnosis only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Clipboard Image Shorthand
Goal: Record the user's shorthand for future image inspection requests.
Status: Completed
Done:
- Agreed that when the user asks to "посмотри картинку", it means inspect the image currently stored in the Windows clipboard.
- Future such requests should first try to extract the clipboard bitmap to a temporary PNG and inspect it.
Next: Use the clipboard image extraction path automatically on future "посмотри картинку" requests.
Notes: Process agreement only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Clipboard Image Inspection
Goal: Inspect the current Windows clipboard image for the user.
Status: Completed
Done:
- Extracted the Windows clipboard bitmap to a temporary PNG and opened it for inspection.
- Confirmed the screenshot shows `PolyCopyTrader.Service.exe` running PostgreSQL schema initialization at statement `114/149`, currently around on-chain activity refresh queue schema/index creation.
Next: Let the schema initialization continue unless it stops on an error; after completion, verify service heartbeat and queue/ingestion progress in PostgreSQL.
Notes: Image inspection only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Queue And Range Query Optimization
Goal: Reduce the new database pressure points after moving market enrichment to the metadata refresh queue.
Status: Completed
Done:
- Optimized on-chain block-range lookups in `PostgresAppRepository`: fill, wallet-execution, and trade-detail range queries now use normalized exact `contract_address` comparisons and two `ORDER BY block_number LIMIT 1` index probes instead of `lower(contract_address)` plus aggregate min/max scans.
- Normalized contract-address query parameters before exact comparisons so callers can still pass mixed-case addresses while stored on-chain rows remain lowercase.
- Changed position refresh queue inserts from `ON CONFLICT DO UPDATE` to `ON CONFLICT DO NOTHING`, and only queue token ids that already have wallet executions.
- Changed derived-range metadata, position, and activity queue seeding to read affected tokens/wallets from indexed `polymarket_onchain_wallet_fills` range data instead of `polymarket_onchain_wallet_executions`.
- Changed range queue seeding conflicts to `DO NOTHING` to avoid unnecessary row updates and reduce lock contention with background refresh workers.
- Verified the live PostgreSQL plan for the new fill block-range query uses `ix_polymarket_onchain_fills_contract_block` via forward and backward index-only scans.
Next: Restart the service and monitor `api_errors` for `BackgroundMarketEnrichment`, `BackgroundSync`, and `BackgroundPositionRefresh`; if deadlocks persist, tune worker batch sizes/concurrency next.
Notes: Verification passed: targeted `OnChainIngestionTests|OnChainMarketEnrichmentTests|StorageTests` 26/26; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` 138/138; service build passed; dashboard build passed; `git diff --check` passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post Metadata Queue Restart Check
Goal: Verify PostgreSQL and service health after restarting with the token metadata refresh queue.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed `polymarket_onchain_token_metadata_refresh_queue` and `ix_polymarket_onchain_token_metadata_refresh_queue_next_attempt` exist; queue table size was about 6.6 MB.
- Confirmed service heartbeat is fresh: `PolyCopyTrader.Service` is `Running` in `ReadOnly`, heartbeat age was about 7 seconds at DB time `2026-05-01 19:56:28 UTC`.
- Confirmed the metadata queue is populated and active: about 22,672 queued token ids, 22,664 due now, 8 retried, oldest queued at `2026-05-01 19:53:15 UTC`.
- Confirmed old active missing-metadata DISTINCT scan over `polymarket_onchain_wallet_executions` was not present (`old_metadata_scan_active=0`).
- Confirmed enrichment is progressing in logs with repeated `On-chain token metadata enriched` entries and successful Gamma HTTP 200 responses.
- Confirmed token metadata real categories are being refreshed through `2026-05-01 19:56 UTC`: `Crypto` 6,800, `Sports` 6,206, `Politics` 1,534, `Finance` 858, plus `unknown` 22,672.
- Confirmed category-performance rows are still moving, with newest refresh around `2026-05-01 19:56 UTC` for some categories; `unknown` remains high at 106,461 while backfill catches up.
- Confirmed no blocking chain was present (`blocked_sessions=0`), but two active sessions were older than 2 minutes, mainly autovacuum; active queries included autovacuum, activity queue seeding, and trade-detail metadata updates.
- Noted remaining pressure: last 30 minutes had 3 market-enrichment Npgsql stream timeouts, 2 ingestion stream timeouts, and 1 position-refresh deadlock. Stack traces show market-enrichment timeout now occurs while queuing position refresh after metadata upsert, not while scanning missing metadata; ingestion timeout occurs in `GetPolymarketOnChainFillBlockRangeAsync`.
Next: Keep the service running. If these timeouts continue, optimize the next bottlenecks: deconflict/timeout-tune position refresh queue writes after metadata upsert and make fill block-range checks cheaper.
Notes: DB/log verification only; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Token Metadata Refresh Queue
Goal: Stop market enrichment from repeatedly scanning the large wallet-execution table for missing token metadata.
Status: Completed
Done:
- Added `polymarket_onchain_token_metadata_refresh_queue` with attempt/backoff fields, a next-attempt index, and schema seeding for existing incomplete metadata rows.
- Changed `GetOnChainTokenIdsMissingMetadataAsync` to read due token ids from the metadata refresh queue instead of `polymarket_onchain_wallet_executions`.
- Enqueued token metadata refresh work from new on-chain fills and derived-data rebuild ranges.
- Cleaned completed metadata queue rows after successful category enrichment and rescheduled failed/blank-category rows with short capped backoff.
- Updated test repository behavior, enrichment/ingestion/schema tests, README, and configuration reference.
Next: Restart the service so schema initialization creates the queue, then monitor `polymarket_onchain_token_metadata_refresh_queue`, market-enrichment errors, and `unknown` category counts.
Notes: Verification passed: targeted `OnChainMarketEnrichmentTests|OnChainIngestionTests|StorageTests` 26/26; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` 138/138; service build passed; dashboard build passed; `git diff --check` passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Next Operational Step
Goal: Decide the next task after deploying category-aware signal gates and verifying service/database health.
Status: Completed
Done:
- Reviewed the latest persisted health check and current code references for metadata enrichment, category-performance refresh, and signal gating.
- Chose the next priority as database-pipeline stabilization before further strategy work: the signal path now depends on known market categories and fresh leader/category performance, while the latest check showed heavy refresh queues, deadlocks, and market-enrichment stream timeouts.
- Identified the most important implementation target: stop scanning the large `polymarket_onchain_wallet_executions` table for missing metadata on every enrichment cycle and replace it with a first-class token metadata refresh queue processed with small locked batches.
- Identified a second follow-up target: reduce/deconflict heavy position, performance, and category-performance refresh workers so catch-up and enrichment can progress without deadlock pressure.
Next: Implement a token metadata refresh queue and then tune refresh worker batch sizes/concurrency; after restart, monitor queue ages, category growth, enrichment errors, and signal rejection reason codes.
Notes: Answer-only planning task; no source behavior changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post Gate Deployment Database Check
Goal: Verify PostgreSQL and service health after deploying leader/category signal gates.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed service heartbeat is fresh: `PolyCopyTrader.Service` is `Running` in `ReadOnly`, last heartbeat age was about 4 seconds at DB time `2026-05-01 19:31:02 UTC`.
- Confirmed the deployed service picked up the new signal gate configuration from logs: `Signal requires market category: True` and `Signal requires leader category performance: True`.
- Confirmed no signals were evaluated in the last 2 hours: `signals_total=0`, so the new rejection gates have not yet been exercised by live queued trades.
- Confirmed on-chain ingestion progressed after restart: completed fresh batches through cursor block `85992531` at `2026-05-01 19:21:28 UTC`; logs showed successful fresh batches `85990032-85990531`, `85990532-85991031`, `85991032-85991531`, `85991532-85992031`, and `85992032-85992531`.
- Confirmed metadata/category-performance workers are active: token metadata rows around `38,414` with newest refresh at `2026-05-01 19:31:01 UTC`; wallet/category performance rows around `135,473` with newest refresh at `2026-05-01 19:30:14 UTC`.
- Confirmed category-performance real categories are growing: `Crypto` 11,308, `Sports` 10,779, `Politics` 5,140, `AI` 978, `Finance` 881, plus `unknown` 105,955.
- Confirmed queues remain large but moving: activity queue 118,005, position queue 30,040, performance queue 82,126, category-performance queue 29,115.
- Confirmed no blocking chain was present (`blocker_count=0`), but active autovacuum and several long/heavy worker queries were running.
- Noted operational pressure: API errors in the last hour included 5 position-refresh deadlocks, 4 market-enrichment stream timeouts, 1 ingestion stream timeout, and 1 performance-refresh deadlock. The latest log also showed market enrichment timing out in `GetOnChainTokenIdsMissingMetadataAsync`.
Next: Keep the service running if the goal is catch-up, but optimize/deconflict background workers next: reduce concurrent heavy refresh pressure and make missing-token metadata lookup cheaper so strict signal gates can get known categories faster.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Leader Category Performance Signal Gate
Goal: Wire on-chain market metadata and wallet/category performance into stake decision logic.
Status: Completed
Done:
- Extended `SignalEvaluationContext` with optional `LeaderCategoryPerformance` and added configurable `SignalOptions` gates for known market category, required leader/category performance, minimum resolved sample, ROI, win rate, score, sample quality, stale age, and performance score bonus.
- Added new rejection reason codes for missing/weak/stale leader-category performance and missing market category.
- Updated `SignalProcessor` to resolve `MarketInfo` from `polymarket_onchain_token_metadata`, load `polymarket_onchain_wallet_category_performance` by `(leader_wallet, category)`, and pass both into normal and live preflight signal evaluation.
- Added indexed lookup methods to `IAppRepository`/Postgres/NoOp/test repositories for token metadata and wallet/category performance; Postgres lookup uses exact `(wallet, category)` to use the existing index, with service-side wallet normalization.
- Enabled the strict service defaults in `src/PolyCopyTrader.Service/appsettings.json`: known category and leader category performance are required, with minimum `Low` sample quality, 3 resolved positions, non-negative ROI, win rate at least 50%, score at least 0, and 24-hour freshness.
- Updated README and configuration reference with the new decision gates.
- Added strategy tests for missing category, missing performance, weak performance, and accepted good performance; updated pipeline integration to verify metadata/performance are wired through the processor.
Next: Restart the service after deploying this commit; monitor `SignalRejection` reason codes for `missing_market_category` and `missing_leader_category_performance` to see whether metadata/performance backfill is keeping up.
Notes: Verification passed: targeted `StrategyEngineTests|PipelineIntegrationTests` 22/22 after rerun; full `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 137/137; `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `git diff --check` passed. An initial parallel test/build hit a transient `PolyCopyTrader.Domain.dll` file lock. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Betting Decision Data Sufficiency
Goal: Assess whether the current data model is sufficient for stake decision logic.
Status: Completed
Done:
- Re-read `DefaultSignalEngine`, `DefaultRiskEngine`, `SignalProcessor`, domain signal/risk models, README, and the on-chain serving/category-performance schemas.
- Confirmed the new on-chain tables are sufficient for historical leader/category research: trades, participants, positions, resolved PnL, ROI, win rate, sample quality, category scores, and recency.
- Confirmed the live/paper signal path already requires fresh order book, spread, slippage, leader trade freshness, size, and exposure/risk limits.
- Found an integration gap: `SignalProcessor` currently calls `SignalEvaluationContext` with `MarketInfo = null`, so the signal engine does not yet consume enriched category/end-date metadata from the on-chain/Gamma tables during decisions.
- Found another integration gap: `DefaultSignalEngine` does not yet use `polymarket_onchain_wallet_category_performance` to accept/reject/size trades by leader quality in the specific category; it uses static trader rules and generic scoring.
Next: Wire on-chain leader/category performance and market metadata into signal evaluation, then add explicit decision gates for leader sample quality, category score, ROI/win rate, unresolved exposure, market status, and metadata freshness.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Raw Log Auto-Cleanup Confirmation
Goal: Confirm whether the service already deletes processed blockchain raw logs.
Status: Completed
Done:
- Re-read the call chain around `OnChainIngestionProcessor.IngestBatchAsync`, `PostgresAppRepository.AddPolymarketOnChainFillsAsync`, `RefreshPolymarketOnChainWalletDerivedDataAsync`, and `DeleteProcessedPolymarketOnChainRawLogsAsync`.
- Confirmed automatic cleanup already exists: normal ingestion writes raw logs, decodes fills, upserts serving tables, then calls `DeleteProcessedPolymarketOnChainRawLogsAsync` in the same fill-processing path.
- Confirmed derived-data refresh also calls `DeleteProcessedPolymarketOnChainRawLogsAsync` after rebuilding wallet/serving rows for existing fills.
- Cleanup deletes only rows in the current contract/block range that already have a matching `polymarket_onchain_trade_details(transaction_hash, log_index)` row.
- Clarified that raw logs can still remain when a batch fails after raw-log insertion but before decoded fills/serving rows/cursor completion, or when old backlog has not yet passed through derived refresh.
Next: If `polymarket_onchain_logs` remains large after ingestion stabilizes, run or implement a batched maintenance cleanup for processed rows and inspect why normal cleanup did not catch them earlier.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Raw Log Processing Marker Explanation
Goal: Explain whether already processed blockchain raw log rows are explicitly marked.
Status: Completed
Done:
- Re-read `polymarket_onchain_logs`, `polymarket_onchain_trade_details`, `polymarket_onchain_ingest_cursors`, and ingestion repository code.
- Confirmed there is no explicit `processed_at_utc`, `processed`, or status column on `polymarket_onchain_logs`.
- Confirmed processing is recorded indirectly and idempotently: `polymarket_onchain_fills` stores decoded source rows by unique `(transaction_hash, log_index)`, `polymarket_onchain_trade_details` stores materialized serving rows by primary key `(transaction_hash, log_index)`, and `polymarket_onchain_ingest_cursors.to_block` records the newest completed batch block.
- Confirmed processed raw logs are deleted only when a matching `polymarket_onchain_trade_details` row exists; incomplete raw logs can remain if a batch fails after raw-log insertion and before fill/serving materialization.
Next: Consider adding explicit batch/run audit or cleanup-watermark tables if operational visibility over processed raw logs is needed; adding a processed flag to raw logs is less useful if the retention goal is to delete processed raw rows.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Raw Data Retention Guidance
Goal: Decide whether any PostgreSQL rows are unnecessary and whether raw blockchain logs should be deleted.
Status: Completed
Done:
- Re-read repository docs and storage code for on-chain raw logs, decoded fills, serving tables, and cleanup behavior.
- Confirmed `polymarket_onchain_logs` is intended as temporary raw staging: after decoded fills are materialized into `polymarket_onchain_trade_details`, `DeleteProcessedPolymarketOnChainRawLogsAsync` deletes matching raw logs.
- Confirmed `polymarket_onchain_fills` is the retained audit/rebuild source and should not be purged casually; wallet fills/executions/trade details/positions/performance are indexed serving/derived tables needed for speed.
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets. System catalog sizes showed `polymarket_onchain_logs` around 14 GB with about 2.5M live rows; `pg_stat_user_tables` reported `n_dead_tup=0` and recent autovacuum, so this is live retained raw data, not just dead-table bloat.
- Noted current ingestion state still had cursor/fills around block `85990031` and raw logs around `85990531`; raw logs beyond the cursor are an incomplete/current batch and can be refetched, but deleting them is not the main win.
- Recommended not deleting final tables. Safe cleanup target is processed raw logs that already have a matching `polymarket_onchain_trade_details` row, preferably in small batches and outside peak ingestion. `VACUUM (ANALYZE)` reclaims reusable space; `VACUUM FULL` requires a maintenance window because it takes an exclusive lock.
Next: If disk/DB pressure stays high, implement or run a batched maintenance cleanup for processed `polymarket_onchain_logs`, then optionally add retention for non-core diagnostic/history tables such as HTTP logs, API errors, heartbeats, and trader leaderboard snapshots.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post-Restart Database Ingestion Check
Goal: Verify whether the restarted service is progressing normally in PostgreSQL.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed the service is alive: heartbeat was fresh at `2026-05-01 18:36:26 UTC`, with `RunMode=ReadOnly`.
- Confirmed the restarted service is running the new forward-only ingestion path: the log shows fresh catch-up starting at `FromBlock=85990032` and no historical-backfill setting.
- Confirmed raw on-chain logs are being fetched again: `polymarket_onchain_logs` reached block `85990531`, with `observed_at_utc` around `2026-05-01 18:29:51 UTC` and 56,697 rows observed in the recent window.
- Confirmed decoded fills and cursor had not advanced yet: `polymarket_onchain_fills` remained at block `85990031`, imported at `2026-05-01 07:39:08 UTC`; `polymarket_onchain_ingest_cursors.to_block` also remained `85990031`.
- Latest Polygon block was about `86270134`, so the cursor was still about 280,103 blocks behind.
- Confirmed no active blocking chain in PostgreSQL (`blocker_count=0` for active/waiting sessions), but recent ingestion errors included one stream timeout and one deadlock in the last hour.
- Category/position/performance repair is still moving: category-performance rows now include `Sports` 10,060, `Crypto` 8,913, `Politics` 3,670, `AI` 960, `Finance` 619, plus `unknown` 104,426; queues remain active.
Next: Wait briefly for the first post-restart batch to finish. If no `On-chain batch ingested` log appears and fills/cursor stay at block `85990031` for another 10-15 minutes, reduce `OnChainIngestion:MaxBlockRange` or split batch commits so large raw-log batches cannot stall decoded fills/cursor advancement.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Visual Studio Service Exit Screenshot
Goal: Interpret `D:\1\12.png`, where the service exits after schema scripts.
Status: Completed
Done:
- Inspected `D:\1\12.png`; schema initialization reached statement 140/140 and printed `Scripts processed`, then Visual Studio reported process exit code `-1 (0xffffffff)`.
- Confirmed `Scripts processed` is expected when `PostgresSchemaInitializer.InitializeAsync` completes, but the service process should then continue into `host.RunAsync()` and keep running.
- Inspected `Program.cs`; after schema initialization, all normal service logs go to `bin\Debug\net10.0\logs\polycopytrader-service-*.log`, not to the Visual Studio console.
- Checked the latest debug log: no fatal termination was logged; the service logged startup and background worker activity after schema initialization.
- Concluded the screenshot does not show a normal service lifetime: either the debug process was stopped/killed, or the process exited without a fatal Serilog entry. Schema scripts themselves completed successfully.
Next: If it repeats without manually stopping debugging, inspect the tail of `src\PolyCopyTrader.Service\bin\Debug\net10.0\logs\polycopytrader-service-20260501.log` immediately after exit and verify whether a `PolyCopyTrader.Service` process remains running.
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Restore Fresh On-Chain Ingestion Priority
Goal: Diagnose why `public.polymarket_onchain_fills` stopped growing and keep new-block ingestion ahead of derived-data repair.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed `polymarket_onchain_fills` had 12,518,768 rows, last blockchain time `2026-04-25 06:55:06 UTC`, last imported time `2026-05-01 07:39:08 UTC`, and zero imports in the last 2 hours.
- Confirmed the ingestion cursor was stale at `to_block=85990031`, while latest Polygon block was about `86269574`, leaving about 279,543 blocks behind.
- Confirmed the service heartbeat was fresh, but recent ingestion errors included `Exception while reading from stream`; database activity showed large derived/materialization inserts.
- Changed `OnChainIngestionProcessor` so fresh forward catch-up runs before `RefreshMissingDerivedDataAsync`; cursor advancement/new fill ingestion now happens before repairing existing derived serving tables.
- Added a regression test proving fresh block catch-up happens before existing derived-data repair.
Next: Restart/redeploy the service with this commit, then monitor `polymarket_onchain_ingest_cursors.to_block`, `polymarket_onchain_fills.imported_at_utc`, and ingestion `api_errors`.
Notes: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "FullyQualifiedName~OnChainIngestionTests"` passed 12/12. `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 133/133. `git diff --check` passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Disable Historical On-Chain Backfill
Goal: Stop on-chain ingestion from scanning older historical blocks after the fresh tail is caught up.
Status: Completed
Done:
- Removed the backward historical backfill phase from `OnChainIngestionProcessor`; manual and background on-chain sync now only scan `to_block + 1` through the latest Polygon block.
- Removed `HistoricalBackfillStartUtc` and `BackgroundHistoricalBatchesPerCycle` from active configuration/options, appsettings, validation summary, and documentation.
- Updated Dashboard overview text to show live-tail-only catch-up.
- Updated ingestion tests so existing cursor ranges are not moved backward and old blocks are not requested after fresh catch-up.
Next: Restart the service so the updated ingestion worker is used; existing historical rows already stored in PostgreSQL are not deleted by this change.
Notes: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "FullyQualifiedName~OnChainIngestionTests"` passed 11/11. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 132/132. `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed. An initial parallel dotnet run hit an `AssemblyInfo.cs` file lock; rerunning sequentially passed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Blockchain Date Range Query
Goal: Provide SQL to show the already downloaded blockchain date range.
Status: Completed
Done:
- Re-read schema references for on-chain logs/fills and confirmed `block_timestamp_utc` is stored on decoded fills/executions/trade details, not on raw `polymarket_onchain_logs`.
- Prepared SQL using `public.polymarket_onchain_fills` to show min/max blockchain timestamps, min/max block numbers, and decoded fill count.
- Also noted that `observed_at_utc` is ingestion/update time and should not be used as blockchain date coverage.
Next: Run the provided SQL in PostgreSQL to inspect current coverage.
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Database Pipeline Health Check
Goal: Check whether the PostgreSQL on-chain/category pipeline is progressing normally.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed category propagation is still progressing: `polymarket_onchain_wallet_category_performance` now has `Crypto` 4,379, `Sports` 3,954, `Politics` 1,096, `AI` 877, `Finance` 349, `Pop Culture` 34, and `Weather` 8 rows, plus `unknown` 94,094.
- Confirmed downstream positions also continue gaining real categories: `Sports` 24,596, `Crypto` 18,958, `Politics` 4,658, `AI` 957, `Finance` 735, `Pop Culture` 278, `Science` 111, and `Weather` 8.
- Confirmed queues are active rather than blocked: position refresh queue has 25,765 rows, category-performance queue has 46,286 rows, and queue newest timestamps are within minutes of the check.
- Confirmed no current blocking PID chain: active/waiting database sessions had `blocker_count = 0`; service heartbeat was fresh at 2026-05-01 16:34:22 UTC.
- Noted active backlog and transient pressure: 448,576 position rows have known metadata category but still `unknown` position category; recent API errors include stream timeouts and several deadlocks, but the pipeline continues moving.
Next: Keep the service running and monitor non-`unknown` growth plus queue ages; intervene if queue newest timestamps stop advancing, blocker counts appear, or deadlocks/timeouts accelerate.
Notes: Used the temporary .NET/Npgsql diagnostic console under `%TEMP%`. No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Unknown Category Growth Explanation
Goal: Explain why `unknown` rows in category performance can grow during backfill.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing secrets.
- Confirmed `polymarket_onchain_wallet_category_performance` currently has `unknown` 88,595 rows, but non-unknown rows are also growing: `Sports` 2,165, `AI` 868, `Crypto` 282, `Politics` 79, `Finance` 42.
- Confirmed positions are also gaining real categories: `Sports` 13,038, `Crypto` 11,292, `Politics` 1,977, `AI` 879, `Finance` 478, plus smaller categories.
- Confirmed backlogs remain in both position refresh and category-performance refresh queues, including non-unknown category pairs waiting to be processed.
- Clarified that `unknown` can grow while new chain data is materialized before metadata/category propagation catches up; old unknown aggregates are recalculated/deleted once positions recategorize.
Next: Monitor both unknown and non-unknown category counts plus refresh queues; intervene only if non-unknown growth stalls or queues stop draining.
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Polymarket Market Meaning
Goal: Clarify what "market" means in `markets_traded`.
Status: Completed
Done:
- Clarified that in this context a market is a concrete Polymarket question/condition identified by `condition_id`, not a category or subcategory.
- Explained the hierarchy: broad category, optional event/series grouping, individual market/question/condition, outcomes/tokens, then trades/positions.
- Gave examples such as a tennis set-winner market, a Fed-rate-cut market, and an Israel/Lebanon diplomatic meeting market.
Next: None
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Markets Traded Field Explanation
Goal: Explain `markets_traded` in `public.polymarket_onchain_wallet_category_performance`.
Status: Completed
Done:
- Re-read position refresh SQL and category-performance aggregation SQL.
- Confirmed `markets_traded` is calculated as `COUNT(DISTINCT condition_id)` from `polymarket_onchain_wallet_positions` within one `(wallet, category)` group.
- Clarified that it counts unique Polymarket questions/markets, not executions, token ids, or outcomes; unenriched rows can temporarily use token id as fallback condition id until metadata refresh corrects them.
Next: None
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Performance Recalculation Mechanics
Goal: Explain how `positions_count` changes when positions are recategorized.
Status: Completed
Done:
- Re-read position refresh and wallet-category performance refresh SQL.
- Confirmed position refresh captures existing `(wallet, old_category)` pairs before deleting/rebuilding positions for refreshed tokens, then captures `(wallet, new_category)` pairs after insert and queues both for category-performance recalculation.
- Confirmed category-performance refresh deletes queued `(wallet, category)` rows from `polymarket_onchain_wallet_category_performance` and re-inserts freshly aggregated rows from current `polymarket_onchain_wallet_positions`; if no positions remain for an old pair, the old row stays deleted.
Next: None
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Performance Field Reference Repeat
Goal: Re-describe fields in `public.polymarket_onchain_wallet_category_performance`.
Status: Completed
Done:
- Re-read schema, domain model, and refresh aggregation SQL for `polymarket_onchain_wallet_category_performance`.
- Confirmed one row is one `(wallet, category)` aggregate derived from `polymarket_onchain_wallet_positions`.
- Prepared concise Russian descriptions for all columns, including score, ROI, win rate, sample quality, and timestamp semantics.
Next: None
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Final Category Table Reminder
Goal: Remind the user of the final category-scoped wallet rating table name.
Status: Completed
Done:
- Confirmed the final category-scoped wallet performance table is `public.polymarket_onchain_wallet_category_performance`.
Next: None
Notes: No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Backfill Progress Check
Goal: Confirm whether on-chain category propagation is healthy and only needs time.
Status: Completed
Done:
- Queried PostgreSQL through `POLYCOPYTRADER_POSTGRES_CONNECTION` without printing the connection string.
- Confirmed category propagation is progressing: `polymarket_onchain_wallet_positions` now has `Sports` 2,063 rows, `AI` 868, `Crypto` 141, and `Politics` 59.
- Confirmed `polymarket_onchain_wallet_category_performance` has non-`unknown` rows: `Sports` 1,442, `AI` 868, `Crypto` 85, and `Politics` 11.
- Confirmed backlog remains: `polymarket_onchain_position_refresh_queue` has 18,294 `derived_refresh` and 2,467 `metadata` rows; category-performance queue has 33,103 `unknown`, 466 `Sports`, 48 `Politics`, and 35 `Crypto`.
- Confirmed recent retry pressure remains: 2 `OnChainPositionRefreshWorker:BackgroundPositionRefresh` errors in the last hour, consistent with the earlier deadlocks.
Next: Let the service continue if queues keep changing; if queues stop draining or deadlocks continue, reduce refresh batch sizes or serialize position/category-performance refresh work.
Notes: No repo source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Use Project Database Connection
Goal: Use the same PostgreSQL connection variable as the project and repair category-performance lag.
Status: Completed
Done:
- Confirmed the project uses `POLYCOPYTRADER_POSTGRES_CONNECTION` via `Storage.ConnectionStringEnvironmentVariable`.
- Confirmed that environment variable is present locally; did not print or persist its value.
- Since `psql` was not available in PATH, used a temporary .NET/Npgsql diagnostic console under `%TEMP%` and the existing environment variable.
- Ran diagnostics: metadata had 2,470 categorized token rows; positions had some categorized rows but 224,290 position rows for categorized metadata were still stale; category-performance had only `unknown`; category-performance queue already contained non-`unknown` pairs.
- Manually processed 2,406 non-`unknown` queued `(wallet, category)` pairs using the same aggregation SQL pattern as the worker, guarded by `FOR UPDATE SKIP LOCKED`.
- Verified `polymarket_onchain_wallet_category_performance` now contains `Sports` 1,442 rows, `AI` 868 rows, `Crypto` 85 rows, and `Politics` 11 rows, in addition to `unknown`.
Next: Let position refresh continue draining `polymarket_onchain_position_refresh_queue`; more categories will appear in positions and category performance as stale token positions refresh.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No repo source code changed; only runtime DB diagnostics and a focused refresh-queue aggregation were executed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Database Access Safety Guidance
Goal: Explain how the user can safely let Codex run PostgreSQL diagnostic queries.
Status: Completed
Done:
- Clarified that database access is possible through local shell tools when credentials are provided outside chat.
- Recommended using an existing local application connection string or a temporary least-privilege PostgreSQL role exposed via a local environment variable, without pasting passwords or connection strings into the conversation.
- Identified the minimum useful permissions for current diagnostics: SELECT on public tables and INSERT/UPDATE on on-chain refresh queue tables for manual requeue operations.
Next: If the user sets a local connection environment variable, use it to run SQL diagnostics directly without printing or committing secrets.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Performance Still Unknown
Goal: Explain why `polymarket_onchain_wallet_category_performance` can still contain only `unknown` after token metadata categories start working.
Status: Completed
Done:
- Re-read the refresh chain: token metadata category updates enqueue `polymarket_onchain_position_refresh_queue`; `polymarket_onchain_wallet_positions` then copies metadata categories; `polymarket_onchain_wallet_category_performance` is rebuilt only from positions, not directly from token metadata.
- Confirmed position refresh captures old and new `(wallet, category)` pairs and enqueues category-performance refreshes, so category performance can lag by two background workers after metadata enrichment succeeds.
- Prepared SQL to identify the exact lag point: metadata categories present, positions still unknown, category-performance queue backlog, or a missing manual enqueue for already-categorized positions.
Next: Run the provided SQL checks; if positions have categories but category-performance queue is empty/stale, manually enqueue distinct `(wallet, category)` pairs from `polymarket_onchain_wallet_positions`.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this operational diagnostic. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Event Category Fallback Verification
Goal: Interpret `D:\1\10.png` and `D:\1\11.png` after deploying Gamma event category fallback.
Status: Completed
Done:
- Inspected `D:\1\10.png`: `polymarket_onchain_token_metadata` still has 38,146 total rows and 38,130 successful lookups, but `with_category` increased from 0 to 220 and `without_category` decreased to 37,926; `max_refreshed` is 2026-05-01 17:10:42.355562+03.
- Inspected `D:\1\11.png`: recent `polymarket_http_logs` include `GetEvent`, `succeeded=true`, `status_code=200`, `count=86`.
- Concluded the new Gamma event fallback is running and producing categories, but the remaining categoryless rows need another pass/progress check to distinguish normal backfill progress from unclassifiable events.
Next: Keep running/forcing `POST /refresh-onchain-markets`; monitor `with_category`, category distribution, `GetEvent` calls, and sample recent no-category rows to decide whether another parser rule is needed.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this screenshot diagnostic. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Gamma Event Category Fallback
Goal: Recover on-chain token categories when Gamma market and condition responses omit category fields.
Status: Completed
Done:
- Analyzed the user's sampled response bodies: CLOB market-by-token returns condition ids, and Gamma market-by-condition returns market data but still omits top-level category in the shown cases.
- Verified against live Gamma API samples that linked Gamma events can expose category signals through event tags/text even when market responses do not include `category`, `tags`, or `categories`.
- Added `IPolymarketGammaClient.GetEventCategoryAsync` and Gamma `/events/{eventId}` support.
- Added parser support for extracting event id from market raw JSON, parsing event category, and deriving deterministic categories such as Sports, Finance, Politics, Crypto, Weather, AI, Science, and Pop Culture from event tags/text.
- Updated on-chain market enrichment to fetch linked event category before CLOB/condition fallback completes, with a per-run event-category cache to avoid repeated event calls.
- Added parser/client/enrichment tests and updated README, configuration reference, and project memory.
Next: Restart/redeploy the service, run `POST /refresh-onchain-markets`, then recheck `polymarket_onchain_token_metadata.with_category` and downstream position/category-performance refresh queues.
Notes: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "FullyQualifiedName~PolymarketClientTests|FullyQualifiedName~OnChainMarketEnrichmentTests"` passed 29/29. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 132/132. `git diff --check` passed with CRLF warnings only. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Fallback Http Log Check
Goal: Interpret `D:\1\9.png` showing recent market metadata fallback HTTP calls.
Status: Completed
Done:
- Inspected `D:\1\9.png`: recent `polymarket_http_logs` include successful `200` responses for `GetOpenMarketByToken`, `GetClosedMarketByToken`, `GetMarketByToken`, `GetOpenMarketByCondition`, and `GetClosedMarketByCondition`.
- Confirmed the latest CLOB/condition fallback path did run between 2026-05-01 15:59 and 16:46 +03, including 663 CLOB token lookups and 663 open/closed condition lookups.
- Concluded that if metadata still has `with_category = 0`, the remaining problem is response-content/parser/catalog coverage rather than an old service version or an unexecuted fallback branch.
Next: Inspect recent HTTP response bodies for the fallback operations; if they contain no category-like fields, implement a local Gamma event/catalog category backfill keyed by condition/market/event.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this screenshot diagnostic.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Still Missing After Refresh
Goal: Interpret `D:\1\8.png` showing no categories after another metadata refresh.
Status: Completed
Done:
- Inspected `D:\1\8.png`: `polymarket_onchain_token_metadata` still has 38,146 rows, 38,130 successful lookups, 16 failed lookups, `with_category = 0`, `without_category = 38,146`, and `max_refreshed = 2026-05-01 16:42:28.101144+03`.
- Clarified that the screenshot proves metadata was refreshed, but not necessarily that the latest CLOB/condition fallback ran; that must be verified in `polymarket_http_logs` by checking `GetMarketByToken`, `GetOpenMarketByCondition`, and `GetClosedMarketByCondition`.
- Prepared decision SQL: if fallback operations are absent, restart/redeploy the service with commit `1b6366e`; if present and successful but still categoryless, implement local Gamma catalog/event catalog fallback.
Next: Run the HTTP-log operation query and one recent-response sample query to decide whether this is deployment/version drift or a source-data coverage problem.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only diagnostic followup. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post Index Recovery Checks
Goal: Provide the next operational checks after the stalled startup index creation continued.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, and recent schema/enrichment context.
- Prepared a staged verification sequence: confirm schema initialization finished, confirm no active DDL/locks, check queue sizes, verify category enrichment with the new CLOB/condition fallback, then verify position and category-performance refresh progress.
Next: Run the SQL checks in order; the most important success signal is `polymarket_onchain_token_metadata.with_category > 0`, followed by positions/categories and category performance rows moving away from null/unknown.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only operational guidance. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Activity Queue Index Startup Stall
Goal: Advise whether to wait or intervene when schema initialization stalls on an activity queue index.
Status: Completed
Done:
- Inspected `D:\1\7.png`; schema initialization is at statement `105/140`, creating `ix_polymarket_onchain_wallet_activity_refresh_queue_queued`.
- Confirmed from `PostgresSchema.cs` that this is `CREATE INDEX IF NOT EXISTS ... ON polymarket_onchain_wallet_activity_refresh_queue(queued_at_utc)`.
- Prepared PostgreSQL diagnostics using `pg_stat_activity`, `pg_blocking_pids`, `pg_stat_progress_create_index`, and metadata-only table size estimates.
Next: If the index shows progress, wait; if it is waiting on locks or no progress for several minutes, stop other app instances and terminate blockers or cancel/restart schema initialization.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only operational guidance. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Add CLOB Condition Category Fallback
Goal: Add the next category enrichment fallback after screenshots showed refreshed Gamma metadata still has no category fields.
Status: Completed
Done:
- Inspected `D:\1\5.png`: metadata was refreshed recently (`refreshed_last_30m = 1190`), proving the fixed enrichment path had run for at least some rows.
- Inspected `D:\1\6.png`: successful Gamma raw JSON still had null `market.category`, event category, event category label, category label, and tag label across 38,130 rows.
- Added `PolymarketClobMarketByToken`, CLOB `GET /markets-by-token/{token_id}` client support, Gamma lookup by `condition_ids`, and enrichment fallback from blank token metadata to CLOB parent market then Gamma by condition id.
- Added parser/client/enrichment tests and updated docs.
Next: Restart/redeploy the service and run `POST /refresh-onchain-markets` repeatedly or let the background worker process blank-category metadata; then recheck `with_category` and `polymarket_onchain_position_refresh_queue`.
Notes: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "FullyQualifiedName~PolymarketClientTests|FullyQualifiedName~OnChainMarketEnrichmentTests"` passed 23/23. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 126/126. `git diff --check` passed with CRLF warnings only. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Recovery Screenshot Followup
Goal: Interpret the post-fix category verification screenshots.
Status: Completed
Done:
- Inspected `D:\1\3.png`: metadata still has 38,146 rows, 38,130 successful lookups, 16 failed lookups, 0 rows with category, and 38,146 rows without category.
- Inspected `D:\1\4.png`: wallet positions still group entirely under `category = null`, count 2,838,895.
- Concluded the database has not yet been corrected by the new enrichment path; either the service/enrichment has not run with the fixed code, or the stored/refetched Gamma JSON has no usable category fields.
Next: Check recent `last_refreshed_utc`/errors to prove whether new enrichment ran; if it did, inspect `raw_json` category/event/tag fields and implement CLOB/condition-id fallback or a local catalog.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only followup. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Fix Blank Gamma Categories
Goal: Recover category enrichment when Gamma metadata rows exist but `category` is blank for every token.
Status: Completed
Done:
- Inspected the user's screenshots: `polymarket_onchain_token_metadata` had 38,146 rows, 38,130 successful lookups, 16 failed lookups, and 0 rows with category.
- Updated Gamma metadata parsing to derive category from `market.category`, then nested event/category/tag fallbacks when the top-level category is missing.
- Updated missing-metadata selection so rows with failed lookup or blank category are retried instead of being treated as complete.
- Updated market enrichment to attempt each token at most once per refresh run, preventing failed/blank tokens from being re-requested repeatedly inside the same cycle.
- Added parser/enrichment tests and documentation notes.
Next: Restart/redeploy the service and run `POST /refresh-onchain-markets`; then monitor metadata categories, position refresh queue, positions without category, and category performance rows.
Notes: `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore --filter "FullyQualifiedName~PolymarketClientTests|FullyQualifiedName~OnChainMarketEnrichmentTests"` passed 20/20. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 123/123. `git diff --check` passed with CRLF warnings only. `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category Enrichment Recovery Options
Goal: Propose ways to fix all on-chain positions having `unknown` category.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, on-chain metadata enrichment configuration, worker, processor, repository SQL, Gamma parser, position refresh SQL, and official Polymarket market docs.
- Identified the main current risk: `GetOnChainTokenIdsMissingMetadataAsync` only retries token ids with no metadata row; rows already marked `lookup_succeeded=false` or with blank category can stop being retried automatically.
- Prepared a staged recovery proposal: diagnose metadata/position/queue health, fix retry semantics, add CLOB condition-id fallback, build a local market catalog, add category source/confidence, and exclude unknown categories from category-sensitive scoring/trading until resolved.
Next: Implement the chosen recovery path, starting with robust metadata retry/backfill and automatic requeue of positions/category performance after category changes.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only planning task. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Unknown Onchain Category Explanation
Goal: Explain why `polymarket_onchain_wallet_category_performance.category` can be `unknown`.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, on-chain fill parser, Gamma client/parser, market enrichment worker, schema, position refresh SQL, and category performance aggregation SQL.
- Confirmed blockchain `OrderFilled` logs provide token ids, wallets, amounts, fees, and related event fields, but not the human market category.
- Confirmed category is pulled from Gamma `/markets?clob_token_ids=...` into `polymarket_onchain_token_metadata`, copied into wallet positions, then coalesced to `unknown` during category-performance aggregation when missing or blank.
Next: Diagnose `unknown` rows by checking missing/failed Gamma metadata and position rows with null or blank category.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Wallet Category Performance Field Reference
Goal: Describe every column in `polymarket_onchain_wallet_category_performance`.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, schema, domain model, repository read SQL, refresh SQL, queue seeding SQL, and category refresh worker.
- Confirmed one row represents one `(wallet, category)` aggregate derived from `polymarket_onchain_wallet_positions`.
- Prepared detailed field descriptions, score/sample-quality formulas, indexes, refresh behavior, and caveats.
Next: Use the table for fast category-specific wallet rating queries; tune the score formula later if real sampled results show a better ranking signal.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task. Existing unrelated dirty files `PolyCopyTrader.sln` and `src/PolyCopyTrader.Storage/PostgresSchemaInitializer.cs` were left untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Wallet Category Performance Table
Goal: Materialize fast category-specific on-chain wallet ratings and keep them updated as new data arrives.
Status: Completed
Done:
- Added `polymarket_onchain_wallet_category_performance` and `polymarket_onchain_wallet_category_performance_refresh_queue` to PostgreSQL schema and required tables.
- Added typed domain models, repository read/refresh contract, PostgreSQL implementation, no-op/test implementations, and in-memory test coverage.
- The position refresh path now captures old and new `(wallet, category)` pairs for affected tokens and enqueues category-performance refreshes, so category scores update when new fills arrive or token metadata recategorizes positions.
- Added `OnChainCategoryPerformanceRefreshWorker` plus service registration, config options, validation, appsettings entries, README, and configuration reference updates.
Next: Restart the service so schema initialization creates the table/queue; monitor `polymarket_onchain_wallet_category_performance` and its refresh queue while position refresh continues.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: initial parallel service/dashboard build hit a Defender file lock on `Domain.dll`; rerun service build passed, dashboard build passed, `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 121/121, `git diff --check` passed with CRLF warnings only. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Category User Rating Capability
Goal: Clarify whether category-specific on-chain user ratings can be computed now.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, schema, repository scoring SQL, configuration docs, and Git state.
- Confirmed `category` is available in `polymarket_onchain_trade_details` and `polymarket_onchain_wallet_positions` after token metadata enrichment.
- Confirmed existing persisted `polymarket_onchain_wallet_performance` and `polymarket_onchain_participant_details` scores are wallet-wide, not `wallet + category` materializations.
- Prepared a category-scoped rating SQL that reuses the current wallet performance scoring formula over `polymarket_onchain_wallet_positions`.
Next: If category rating speed matters in the Dashboard, materialize a dedicated indexed `wallet_category_performance`/`participant_category_details` table instead of running grouped SQL ad hoc.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Table Completeness Answer
Goal: Clarify whether all necessary on-chain research tables now exist.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, README on-chain docs, schema table declarations, and Git state.
- Confirmed the schema now includes the necessary current-stage tables for decoded fills, wallet fills/executions, trade details, token metadata, wallet activity, participant details, positions, performance, queues, raw-log staging, and ingest cursors.
- Clarified that table existence is no longer the main risk; the remaining work is verifying that data backfill/enrichment has caught up and queues drain without errors.
Next: Monitor row counts/ranges, queue sizes, metadata enrichment, and `api_errors`; only add more schema if a specific missing analysis question appears.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Result Table Count SQL
Goal: List the resulting on-chain research tables and provide a count-control SQL query.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, on-chain README/config docs, schema table declarations, and Git state.
- Confirmed the resulting fast research/materialized tables are wallet fills, wallet executions, token metadata, trade details, wallet activity, participant details, wallet positions, and wallet performance.
- Prepared a count-control query that includes the retained audit source `polymarket_onchain_fills`, raw-log staging `polymarket_onchain_logs`, resulting tables, refresh queues, and ingest cursors.
Next: Run the count-control SQL periodically during backfill and watch source/result counts catch up while queues drain.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Current Onchain Pipeline Purpose
Goal: Clarify what the current operational phase is doing.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, README on-chain section, relevant schema/repository/processor references, and Git state.
- Clarified that the current phase is not only filling user tables; it is building the fast research/serving layer from already decoded on-chain fills.
- Summarized the pipeline as decoded fills to wallet fills/executions, trade details, activity/participant/position/performance summaries, plus market metadata enrichment.
Next: Continue monitoring derived table counts/ranges until serving tables catch up, then verify Dashboard speed and completeness before tuning retention/purge behavior.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Activity Queue Growth Guidance
Goal: Explain what it means when `polymarket_onchain_wallet_activity_refresh_queue` grows during initial on-chain backfill.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, activity queue schema, activity refresh repository logic, worker code, service config, and Git state.
- Confirmed the activity queue is keyed by wallet and can grow normally while derived backfill discovers unique wallets faster than `OnChainActivityRefreshWorker` processes them.
- Confirmed default throughput is conservative: `ActivityRefreshWalletBatchSize=100` every `ActivityRefreshIntervalSeconds=30`, plus seed batches of `500`.
- Prepared diagnostics to distinguish normal backlog from a stuck worker: activity rows/refreshed timestamps, queue reasons/oldest age, `OnChainActivityRefreshWorker` errors, and pg_stat_activity for long refresh statements.
Next: If activity rows are growing and no errors exist, either wait or raise activity refresh batch/interval settings; if rows are not growing, inspect worker errors/locks first.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Next Operational Step After Schema
Goal: Clarify the next operational step after successful startup schema SQL.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, README IPC/on-chain sections, local control server endpoints, on-chain ingestion worker, and Git state.
- Confirmed the next milestone is running the service and letting/forcing on-chain ingestion to call `RefreshMissingDerivedDataAsync`, which backfills existing `polymarket_onchain_fills` into serving tables before continuing fresh/historical scanning.
- Identified manual trigger endpoint `POST http://127.0.0.1:5118/refresh-onchain`, status endpoint `GET /status`, and dashboard button `Onchain sync`.
Next: Start the service, trigger or wait for on-chain sync, watch logs for serving-data refresh messages, and monitor counts/ranges until wallet/trade serving tables become non-zero and catch up.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Post Schema Startup Verification
Goal: Provide the next database checks after startup SQL completed successfully.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, relevant on-chain schema/docs/code, and Git state.
- Confirmed after schema initialization the next critical verification is derived-data backfill from `polymarket_onchain_fills` into `polymarket_onchain_wallet_fills`, `polymarket_onchain_wallet_executions`, `polymarket_onchain_trade_details`, and participant/activity/position/performance layers.
- Prepared SQL checks for blockers, object kinds, index validity, approximate sizes, exact bounded counts, derived range coverage, raw-log cleanup, queues, errors, heartbeats, and dashboard-visible smoke queries.
Next: Start/restart the service, wait a few minutes, then run the SQL checks in the recommended order.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 PostgreSQL Unblock Runbook
Goal: Provide a concrete runbook to unblock PostgreSQL sessions stuck around schema/index/count operations.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Prepared a safe operational sequence: stop the application sources of new DB queries, inspect real blockers with `pg_stat_activity`/`pg_blocking_pids`, terminate blocking backends, and restart PostgreSQL only as a last resort.
- Clarified that `trader_leaderboard_snapshots` can be dropped/truncated after unblocking only if losing Trader Discovery leaderboard snapshot history is acceptable; it is not part of the on-chain raw/fill tables.
Next: Stop the service/Dashboard, run the blocker diagnostic from a fresh SQL session, terminate blocker pids, then restart PostgreSQL only if termination cannot clear the stuck backends.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 PgAdmin Lock Screenshot Interpretation
Goal: Interpret the user's pgAdmin screenshot during PostgreSQL lock debugging.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Opened `D:\1\Img1.png` and confirmed the visible grid is a count result, not a `pg_locks`/`pg_stat_activity` lock report.
- Identified that the pgAdmin lock icons in the result column headers indicate read-only result columns, not active PostgreSQL locks.
- Noted the visible counts: raw fills are `2,012,149`, while `wallet_fills` and `wallet_executions` are still `0`.
Next: Run the `pg_stat_activity`/`pg_blocking_pids` diagnostic query to inspect real database blockers.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 PostgreSQL Cancel Escalation Guidance
Goal: Explain what to do when a PostgreSQL query on `trader_leaderboard_snapshots` cannot be cancelled.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Clarified that `pg_cancel_backend` can fail or appear ineffective when the wrong pid is targeted, privileges are insufficient, the backend is blocked behind another session, or PostgreSQL is rolling back a cancelled DDL/index operation.
- Prepared an escalation path: identify blockers with `pg_stat_activity`/`pg_blocking_pids`, terminate the blocking backend with `pg_terminate_backend`, then monitor cleanup/rollback instead of repeatedly cancelling the blocked `count(*)`.
Next: Run the blocker diagnostic query, terminate the actual blocking backend if needed, and wait if PostgreSQL is cleaning up an aborted index build.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Leaderboard Count Lock Guidance
Goal: Explain why `count(*)` on `trader_leaderboard_snapshots` hangs after cancelling index creation and how to inspect size without scanning.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Clarified that `count(*)` can hang because it waits for an `AccessShareLock` behind DDL/rollback locks, not necessarily because the table is enormous.
- Prepared metadata-only size/estimated-row SQL using `pg_class`, `pg_relation_size`, `pg_indexes_size`, and lock diagnostic SQL using `pg_stat_activity`/`pg_blocking_pids`.
Next: Run the lock diagnostic query first; if `count(*)` is blocked, cancel/terminate the blocking schema/index backend or wait for rollback to finish.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Trader Leaderboard Snapshots Explanation
Goal: Explain the purpose of `trader_leaderboard_snapshots` and why `count(*)` can hang.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, schema, repository writes, TraderDiscovery processor, README, and project memory.
- Confirmed `trader_leaderboard_snapshots` is the current Polymarket leaderboard snapshot pool used by manual Trader Discovery/Find traders, not the on-chain ingestion pipeline.
- Confirmed schema initialization still has legacy migration work for this table: updates, duplicate collapse, deletes, dropped legacy columns, and leaderboard indexes.
- Explained that `count(*)` can hang because PostgreSQL `count(*)` scans the table and can also wait behind schema DDL locks.
Next: If it is blocking startup and Trader Discovery history is not needed, consider truncating/resetting this table or moving its migration out of startup schema initialization.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Stepwise Schema Initialization
Goal: Split PostgreSQL schema initialization into individually debuggable commands.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Updated `PostgresSchemaInitializer.InitializeAsync()` to split `PostgresSchema.SchemaSql` into individual SQL statements and execute them one by one.
- Kept `CommandTimeout = 0` on each individual schema command.
- Kept the local `try/catch` with console output and rethrow for debugging.
- Added a SQL splitter that preserves dollar-quoted `DO $$ ... $$;` blocks and quoted strings, plus unit tests for current schema splitting and dollar-quoted blocks.
Next: Rebuild/republish/restart the service; set breakpoints on the `for` loop or `ExecuteNonQueryAsync` to step through each schema statement and inspect `statementIndex`/`commandText`.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 121/121; `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `git diff --check` passed with CRLF warnings only. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Schema Initialization Interrupt Safety
Goal: Explain whether interrupting a long PostgreSQL schema initialization is dangerous.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Clarified that interrupting normal PostgreSQL DDL/index creation should not corrupt data; PostgreSQL rolls back the active interrupted statement/transaction.
- Clarified the practical risks: wasted index build time, incomplete schema initialization, and service startup remaining blocked until schema init is rerun successfully.
- Prepared a safe interrupt order: check progress/locks first, prefer graceful service stop/cancel over killing PostgreSQL, then rerun schema verification.
Next: Use PostgreSQL progress/lock checks before interrupting if possible.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Long Schema Initialization Guidance
Goal: Explain whether a one-hour PostgreSQL schema initialization run is expected and how to diagnose it.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, and `PostgresSchema.cs`.
- Confirmed the new `polymarket_onchain_trade_details` and `polymarket_onchain_participant_details` tables/indexes should be quick when first created because they start empty.
- Identified the likely one-hour causes as missing index creation on existing large on-chain tables or lock waits from another active session.
- Prepared SQL diagnostics using `pg_stat_progress_create_index`, `pg_stat_activity`, and `pg_blocking_pids`.
Next: Run the PostgreSQL diagnostic SQL against the same database while schema initialization is still running.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Schema Initializer Catch Restore
Goal: Restore a local catch block in schema initialization for debugging while keeping timeout behavior.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Wrapped `PostgresSchemaInitializer.InitializeAsync()` in `try/catch`, writes the caught exception to console, then rethrows it.
- Kept `command.CommandTimeout = 0` so long schema DDL/index creation is not cancelled by Npgsql's default command timeout.
Next: Put a breakpoint in the `catch` block while debugging schema initialization.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119; `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `git diff --check` passed with CRLF warnings only. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Schema Initialization Timeout Fix
Goal: Stop PostgreSQL schema initialization from failing on long-running index/table setup for large on-chain data.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, and schema initializer code.
- Diagnosed `Exception while reading from stream` with inner `Timeout during reading attempt` as the default Npgsql command timeout expiring while schema SQL runs.
- Set `PostgresSchemaInitializer` command timeout to `0` for the startup schema script so long `CREATE INDEX IF NOT EXISTS`/DDL operations can complete.
- Removed the local swallowed exception state from the initializer by restoring normal exception propagation; service startup now fails loudly if schema initialization truly fails.
Next: Rebuild/republish/restart the service, then rerun the table-kind SQL check.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119; `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `git diff --check` passed with CRLF warnings only. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Schema Initializer Breakpoint Guidance
Goal: Explain where to debug empty on-chain serving table schema checks.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, `Program.cs`, `PostgresSchemaInitializer.cs`, and the on-chain serving table section of `PostgresSchema.cs`.
- Identified the exact breakpoint path: `Program.cs` calls `IStorageSchemaInitializer.InitializeAsync()` before `host.RunAsync()`, which runs `PostgresSchemaInitializer.InitializeAsync()` and executes `PostgresSchema.SchemaSql`.
- Prepared diagnostic checks for old published service binaries, wrong PostgreSQL database connection, and schema initialization exceptions.
Next: Run the service under debugger from the current source or attach to the actual Windows Service process and inspect the schema initializer connection/command.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Serving Verification Guidance
Goal: Provide concrete checks for the indexed on-chain serving tables and raw log purge behavior.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, docs, README, and Git state.
- Prepared verification steps: restart service/schema initializer, confirm explorer objects are tables with indexes, run on-chain sync, compare decoded fills versus trade details, verify participant rows and refresh queues, verify processed raw logs are purged, and use `EXPLAIN ANALYZE` for speed checks.
Next: Run the SQL checks against the local PostgreSQL database after restarting the service.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Fast Onchain Serving Tables And Raw Log Purge
Goal: Prioritize fast on-chain query surfaces and remove old raw blockchain logs only after they are processed into indexed serving tables.
Status: Completed
Done:
- Replaced the on-chain trade and participant explorer views with physical indexed PostgreSQL tables `polymarket_onchain_trade_details` and `polymarket_onchain_participant_details`.
- Added incremental trade-detail upserts from decoded fills, metadata refresh propagation from Gamma token metadata, and ingestion/derived-range checks that backfill missing trade-detail ranges from existing decoded fills.
- Added participant-detail refresh from activity, position, and performance refresh paths, including initial queue seeding for existing activity rows missing participant summaries.
- Added raw `polymarket_onchain_logs` cleanup after decoded fills have been materialized into `polymarket_onchain_trade_details`; decoded fills remain retained as the audit/rebuild source.
- Updated Dashboard-facing repository reads through the same table names, tests, README, configuration reference, and project memory.
Next: Restart the service so schema initialization converts the old views into indexed tables and the next on-chain sync backfills/purges processed raw logs.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119; `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `git diff --check` passed with CRLF warnings only. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Serving Tables Architecture
Goal: Align on the target architecture for blockchain ingestion, indexed derived tables, and raw data retention.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Clarified the target architecture: ingest blockchain logs, transform them into indexed serving tables, switch Dashboard/analysis to those tables, then purge/archive raw logs only after coverage validation.
- Clarified that current raw data should not be deleted first unless we intentionally choose a full reset; existing raw fills can be used to backfill the new serving tables.
- Proposed replacing ordinary explorer views with physical tables such as on-chain trades, trade participants, participant summaries, and indexed wallet/time/token/market access paths.
Next: Implement physical indexed on-chain trade/participant serving tables, refresh/upsert them from existing fills and future ingestion ranges, switch Dashboard reads to those tables, then add optional raw-retention purge.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is context/Git inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Views Versus Tables Speed Answer
Goal: Explain why on-chain explorer was initially added as views and clarify the faster target design.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, and Git state.
- Clarified that ordinary PostgreSQL views are saved queries, not cached storage, so they are not the final best design when Dashboard/query speed over large on-chain data matters.
- Clarified the views were chosen as a low-risk first analyst surface with no extra backfill/refresh invalidation logic, but the next speed-focused step should convert trade/participant explorer surfaces into indexed materialized tables maintained incrementally.
Next: Replace `polymarket_onchain_trade_details` and `polymarket_onchain_participant_details` views with indexed tables plus refresh queues/workers or repository refresh methods.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is context/Git inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Missing Onchain Trade Details View Guidance
Goal: Explain why `public.polymarket_onchain_trade_details` is missing and how to create it.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, service schema registration, local service script, schema SQL, README, and configuration reference.
- Confirmed `polymarket_onchain_trade_details` and `polymarket_onchain_participant_details` are present in `PostgresSchema.SchemaSql` and created by `PostgresSchemaInitializer`.
- Confirmed `PolyCopyTrader.Service` registers `PostgresSchemaInitializer`; Dashboard does not own schema initialization.
- Explained that the local PostgreSQL database has not yet been initialized by the updated service build, or the SQL client is connected to a different database.
Next: Restart the updated service to run schema initialization, then verify the views via `information_schema.views`.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is code/config/docs inspection. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Trade Participant Explorer
Goal: Move the on-chain pipeline toward complete trade and participant exploration after blockchain scanning.
Status: Completed
Done:
- Added read-only PostgreSQL views `polymarket_onchain_trade_details` and `polymarket_onchain_participant_details`.
- Added typed domain records and repository reads for enriched trade details and participant summaries.
- Added Dashboard tabs `Onchain Trades` and `Onchain Participants`.
- Added `OnChainTrades.csv` and `OnChainParticipants.csv` to Dashboard CSV export.
- Updated README, configuration reference, project memory, schema tests, no-op/test repositories, and scanner fake repository.
Next: Add wallet drilldown/filtering so selecting a participant can show all of that wallet's trades, positions, counterparties, and market metadata beyond the current top/recent Dashboard lists.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. Verification: `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed; `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed; first parallel test attempt hit an `obj\Verify` file lock from concurrent service build, then `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119; `git diff --check` passed with only CRLF warnings. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Data Goal Gap Answer
Goal: Assess whether the current on-chain pipeline satisfies the target model of complete trades and participant history.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, project memory, Git state, on-chain schema, repository read/write paths, Dashboard mappings, README, and configuration reference.
- Confirmed the core DB layers exist: raw logs, decoded fills, maker/taker wallet fills, wallet executions, token metadata, activity, positions, performance, and cursors.
- Confirmed the target is only partially complete as a product surface: there is no dedicated participant table, no full wallet drilldown API/UI, Dashboard lists are limited/top/recent views, and some enriched deal details depend on Gamma metadata and refresh queues.
Next: Implement a first-class on-chain participant/trade exploration layer if the next task is to make this goal operational: participant table/materialized view, full trade view, wallet drilldown, SQL/backfill checks, and Dashboard drilldown/export.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is code/schema/docs inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Observed At Explanation
Goal: Explain whether `polymarket_onchain_logs.observed_at_utc` min/max means logs are limited to one day.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, ingestion code, repository upsert SQL, parser mapping, README, and configuration reference.
- Confirmed `observed_at_utc` is the ingestion observation time (`DateTimeOffset.UtcNow` per batch), not the blockchain event time.
- Confirmed `polymarket_onchain_logs` upserts on `transaction_hash, log_index` and updates `observed_at_utc`, so repeat ingestion can make old chain logs look newly observed.
- Found no pruning/retention path for `polymarket_onchain_logs`; history depth should be checked by `block_number`, `polymarket_onchain_fills.block_timestamp_utc`, and `polymarket_onchain_ingest_cursors`.
Next: If needed, add `block_timestamp_utc` to `polymarket_onchain_logs` for raw-log coverage queries without relying on decoded fills.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is code/schema/docs inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-05-01 Onchain Raw Logs Table Answer
Goal: Identify where downloaded blockchain event logs are stored.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, project memory, Git state, schema, repository writes, ingestion processor, README, and configuration reference.
- Confirmed raw Polygon `OrderFilled` event logs are persisted to `polymarket_onchain_logs`; decoded fill rows are persisted to `polymarket_onchain_fills`.
Next: Use row counts or sampled SQL queries if operational verification of the local database contents is needed.
Notes: `git rev-parse --abbrev-ref --symbolic-full-name '@{u}'` failed because branch `master` has no configured upstream, so pull/push cannot run automatically. No source code changed for this answer-only task; verification is code/schema/docs inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Empty Leader Trades Table Answer
Goal: Explain why `leader_trades` is empty while on-chain data is loading.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, watchlist scanner, repository writes, appsettings watchlist sections, README, and project memory.
- Confirmed `leader_trades` is populated only by `WatchlistScanner` from configured enabled `Watchlist:Traders` via Polymarket Data API `GetUserTradesAsync`.
- Confirmed the sample service watchlist contains disabled placeholder `0xPLACEHOLDER`, Dashboard watchlist config is empty, and on-chain ingestion writes to `polymarket_onchain_*` tables instead of `leader_trades`.
Next: Inspect `scanner_status` and `api_errors` if an enabled watchlist is expected to be scanning; use on-chain tables for blockchain-derived wallet trades.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; verification is code/config/docs inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Empty Traders Table Answer
Goal: Explain why `public.traders` is empty.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, schema, repository interface, scanner, Dashboard data service, and appsettings watchlist sections.
- Confirmed `public.traders` is only created by schema; current repository code has no `INSERT INTO traders` and no methods that read/write it.
- Confirmed the active watchlist is read from `Watchlist:Traders` configuration, while discovery and on-chain wallets are persisted to `trader_discovery_candidates`, `trader_leaderboard_snapshots`, and `polymarket_onchain_*` materialized tables.
Next: Decide whether to leave `traders`/`trader_rules` as future DB-backed watchlist tables or implement a config-to-DB/watchlist persistence feature.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; verification is code/schema/config inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Database Table Inventory Answer
Goal: Provide a concise inventory of PostgreSQL tables and their purpose.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, Git state, and `PostgresSchema`.
- Extracted the current required PostgreSQL table list from `src/PolyCopyTrader.Storage/PostgresSchema.cs`.
- Prepared grouped descriptions for core traders, signals/orders, diagnostics, on-chain ingestion/research, and service status tables.
Next: Use SQL row counts when operational verification of actual database contents is needed.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; verification is schema inspection and `git diff --check`. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Onchain Pipeline Boundary Answer
Goal: Clarify whether the implemented on-chain workflow is a complete chain from Polygon ingestion to wallet/player and bet views.
Status: Completed
Done:
- Re-read workflow, project rules, coding rules, active context, README, configuration reference, service worker registration, and Dashboard on-chain bindings.
- Confirmed the read-only research chain exists from Polygon `OrderFilled` logs through raw fills, wallet fills, wallet executions, token metadata enrichment, materialized wallet activity, positions, and performance tables into Dashboard tabs.
- Clarified the limitation that this is not yet an automatic copy-signal decision layer: data catch-up, market enrichment, resolved PnL quality, and later score tuning still matter.
Next: Validate sampled on-chain rows against Polymarket/Data API and add a wallet drilldown if deeper inspection of one player is needed.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; relevant docs/code were inspected. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Dashboard Restart Verification Guidance
Goal: Explain what to verify after the Dashboard window starts again.
Status: Completed
Done:
- Re-read the repository workflow, project rules, coding rules, active context, and current Git state before answering.
- Confirmed the latest behavior: Dashboard startup no longer runs blocking schema initialization; the service owns schema creation and background filling of `polymarket_onchain_wallet_activity`.
- Prepared UI and SQL checks for Dashboard errors, service-created activity tables, activity worker progress, and first ranking rows.
Next: Verify the service-created schema and activity refresh progress; `Onchain Rankings` can be empty until `polymarket_onchain_wallet_activity` has rows.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; verification is limited to repository context reads and Git state inspection. Existing unrelated `PolyCopyTrader.sln` changes remain untouched.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Unblock Dashboard Startup
Goal: Restore WPF Dashboard window startup after schema initialization blocked UI creation.
Status: Completed
Done:
- Diagnosed Dashboard not showing as synchronous `PostgresSchemaInitializer` running inside `DashboardRepositoryFactory.Create()` before the WPF window could be displayed.
- Removed blocking schema initialization from Dashboard startup; service owns schema initialization again.
- Updated `PostgresAppRepository.GetTraderOnChainStatsAsync` to return an empty Onchain Rankings result when `polymarket_onchain_wallet_activity` does not exist yet instead of failing refresh.
- Updated `README.md` and `Codex/20_PROJECT_MEMORY.md` to document that Dashboard does not run migrations on startup and gracefully handles missing activity table.
Next: Restart Dashboard. Restart service separately to create/fill `polymarket_onchain_wallet_activity`; until then `Onchain Rankings` can be empty without blocking the rest of the dashboard.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. The first parallel service build hit a temporary Defender file lock, then `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `git diff --check` passed. Existing unrelated `PolyCopyTrader.sln` changes were left untouched and not included in this task.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Dashboard Schema Initialization
Goal: Fix Dashboard refresh failure when a newly added PostgreSQL table does not exist yet.
Status: Completed
Done:
- Diagnosed `42P01: relation "polymarket_onchain_wallet_activity" does not exist` as Dashboard reading a table before schema initialization had run for the updated code.
- Updated `src/PolyCopyTrader.Dashboard/Services/DashboardRepositoryFactory.cs` so Dashboard runs `PostgresSchemaInitializer` before creating `PostgresAppRepository` when PostgreSQL storage is configured.
- Updated `README.md` and `Codex/20_PROJECT_MEMORY.md` to document Dashboard schema initialization.
Next: Restart Dashboard. It should create missing schema objects before the first refresh; service restart is still recommended so background workers fill the new activity table.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `git diff --check` passed. Existing unrelated `PolyCopyTrader.sln` changes were left untouched and not included in this task.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Materialize Onchain Activity Ranking
Goal: Stop Dashboard refresh timeouts caused by `GetTraderOnChainStatsAsync` grouping millions of on-chain wallet execution rows.
Status: Completed
Done:
- Diagnosed the copied Dashboard error as an Npgsql read timeout in `PostgresAppRepository.GetTraderOnChainStatsAsync`, called from `DashboardDataService.LoadAsync`.
- Added `polymarket_onchain_wallet_activity` and `polymarket_onchain_wallet_activity_refresh_queue` schema objects.
- Changed `GetTraderOnChainStatsAsync` to read the materialized activity table instead of aggregating `polymarket_onchain_wallet_executions` on every Dashboard refresh.
- Added `RefreshPolymarketOnChainWalletActivityAsync` and queue seeding/refresh helpers in `src/PolyCopyTrader.Storage/PostgresAppRepository.cs`.
- Added `OnChainActivityRefreshWorker`, config options, validation, appsettings entries, service registration, no-op/test repository implementations, and schema/config tests.
- Added `ix_polymarket_onchain_wallet_executions_recent` to support the recent executions dashboard query.
- Updated `README.md`, `docs/configuration_reference.md`, and `Codex/20_PROJECT_MEMORY.md`.
Next: Restart the service first so schema initialization creates the new activity tables and the activity worker starts; then restart Dashboard. `Onchain Rankings` may be empty until the background worker fills the activity table.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `git diff --check` passed. Existing unrelated `PolyCopyTrader.sln` changes were left untouched and not included in this task.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Copyable Dashboard Errors
Goal: Make Dashboard Errors rows expand to full message/details height and make errors easy to copy.
Status: Completed
Done:
- Viewed `D:/1/img2.png` and confirmed `Dashboard Errors` rows were clipped by fixed DataGrid row height.
- Updated `src/PolyCopyTrader.Dashboard/MainWindow.xaml` so the `Dashboard Errors` grid overrides fixed row height with auto row sizing, wraps message/details text, uses read-only text boxes for selectable/copyable cell text, and supports selected-row clipboard copy.
- Updated `src/PolyCopyTrader.Dashboard/ViewModels/MainViewModel.cs` with selected-error state and `CopySelectedDashboardErrorCommand`.
- Updated `README.md` and `Codex/20_PROJECT_MEMORY.md` to note wrapped/copyable dashboard error details.
Next: Restart Dashboard and use `Dashboard Errors` -> `Copy selected`, or select text directly inside Message/Details.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `git diff --check` passed. Existing unrelated `PolyCopyTrader.sln` changes were left untouched and not included in this task.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Dashboard Error History Tab
Goal: Keep transient WPF Dashboard errors visible after the footer message is cleared by the next refresh.
Status: Completed
Done:
- Added `DashboardErrorRow` to `src/PolyCopyTrader.Dashboard/Models/DashboardRows.cs`.
- Added `DashboardErrors` in-memory collection, `ClearDashboardErrorsCommand`, and error recording helpers in `src/PolyCopyTrader.Dashboard/ViewModels/MainViewModel.cs`.
- Dashboard refresh exceptions, IPC command exceptions, rejected IPC responses, and CSV export exceptions now append newest-first rows with UTC time, source, message, and details.
- Added a `Dashboard Errors` tab and `Clear errors` buttons in `src/PolyCopyTrader.Dashboard/MainWindow.xaml`.
- Updated `README.md` and `Codex/20_PROJECT_MEMORY.md`.
Next: Restart or rebuild the dashboard; future transient refresh/IPC/CSV errors will accumulate in the new tab during the dashboard session.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `git diff --check` passed. Existing unrelated `PolyCopyTrader.sln` changes were left untouched and not included in this task.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Startup Files Answer
Goal: Answer which repository files Codex rereads during protocol startup and task initialization.
Status: Completed
Done:
- Re-read `Codex/Rules/Workflow.md`, `AGENTS.md`, `Codex/Rules/CodingRules.md`, and `Codex/Contexts/ContextPolyCopyTrader.md`.
- Inspected `git status --porcelain=v1` and `git log -1 --oneline`.
- Confirmed exact `start` bootstrap reads workflow, `AGENTS.md`, sorted daily history files, and active context; normal non-`start` prompts also read coding rules, active context, relevant task docs, and Git state.
Next: Continue future tasks from the repository-local workflow and active context.
Notes: `git pull --ff-only` was attempted and still cannot run because branch `master` has no configured upstream. No source code changed for this answer-only task; verification is limited to repository context reads.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

## Active Update 2026-04-30 Adopt Context History Protocol
Goal: Enable repository-local context and history persistence for future Codex work in PolyCopyTrader.
Status: Completed
Done:
- Read `Codex/ContextHistoryProtocol.md`.
- Added `Codex/Rules/Workflow.md` with bootstrap, task initialization, active context, daily history, and finalization rules adapted to this repository.
- Added `Codex/Rules/CodingRules.md` with the project-local C#/.NET safety and engineering constraints.
- Added `ActiveContextFile: Codex/Contexts/ContextPolyCopyTrader.md` to root `AGENTS.md`.
- Created `Codex/Contexts/ContextPolyCopyTrader.md` and daily history under `Codex/Contexts/History`.
- Preserved the previous on-chain leaders state: wallet performance materialization, `Onchain Leaders`, docs, tests, and builds were completed before this protocol adoption.
Next: Continue future tasks by reading workflow, active context, history, project memory, and Git status before acting.
Notes: `git pull --ff-only` was attempted on 2026-04-30 and failed because branch `master` has no configured upstream. `git diff --check` passed. `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore` passed 119/119. `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore` and `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore` passed with 0 warnings and 0 errors. Last commit before adoption was `d9d7984 Update Codex project memory`. The working tree contains the completed on-chain ingestion/leaders changes plus this context protocol setup.
Blockers: Automatic pull/push cannot run until a Git upstream is configured.

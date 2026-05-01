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

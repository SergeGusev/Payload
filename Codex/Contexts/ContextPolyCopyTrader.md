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

# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 18 plus local debugging, trader discovery, Gamma active-market ingestion, and paused on-chain discovery support. It contains project structure, typed configuration, PostgreSQL schema initialization, a basic repository, read-only Polymarket Data/CLOB/Gamma/Geo clients, a Worker Service scanner/signal/paper/live loop, local dashboard controls, public market WebSocket monitoring, trader discovery, Polygon `OrderFilled` ingestion with fresh catch-up for the live tail, analytics reports, CSV export, diagnostics, a monitoring dashboard, L2 API credential bootstrap, L2 HMAC header infrastructure, dry-run CLOB V2 signing, manually gated tiny maker-only live order placement, Windows VPS deployment scripts, and operations runbooks.

## Safety

- Live trading exists only behind `Bot:Mode=Live`, `Bot:EnableLiveTrading=true`, `LiveTrading:ManualEnableCode=LIVE_TRADING_ENABLED`, auth readiness, geoblock, clock-drift, API-error, risk, order-book, and kill-switch gates.
- Implemented live trading is BUY-only, explicitly risk-capped, and disabled by default. The legacy Follow Leader live path remains tightly gated. For opening-limit BTC/ETH/SOL 5-minute strategy variants, the Dashboard/DB `Live` flag (`strategies.live_stakes`) is the runtime switch for the paper/live shadow path: checking `Live` makes the strategy eligible for linked live-shadow orders when the normal live gates pass; Paper simulation and Live submission consume the same immutable BUY FAK intent, with `postOnly=false`, the same cash amount and maximum price, and no GTD expiration. Unchecking `Live` disables new live-shadow entries.
- Private-key handling is limited to secret-provider lookup for dry-run/live signing. Keys are not requested, stored in appsettings, or logged.
- Auth supports secret lookup, L2 HMAC signatures, L2 headers, dry-run CLOB V2 order signing, live order signing/submission, cancellation, and readiness reporting.
- Live order payloads, responses, cancellations, settlement accounting, and live trading events are persisted with secrets and signatures redacted.
- BTC paper/live shadow test decisions, correlation ids, linked Paper/Live orders, and discrepancy records are persisted so one real order can be compared against its Paper-shadow model.
- Paper trading can optionally keep running in `Live` mode with `PaperTrading:RunInLiveMode=true`; this is shadow Paper only and does not relax any live-order gate.
- Default mode is read-only/paper-first by project policy.
- Every Paper execution change is governed by the mandatory [Paper/Live execution parity contract](docs/architecture/PAPER_LIVE_PARITY.md). The default remains: no proven Live equivalent means no Paper trade or Paper PnL claim. The only exception is the exact closed ETH Reference Average Maker-GTD family enumerated in that contract; its TouchNoDepth fills are not Live-equivalent and cannot be generalized.

## Project Structure

```text
src/
  PolyCopyTrader.Domain/
  PolyCopyTrader.Polymarket/
  PolyCopyTrader.Strategy/
  PolyCopyTrader.Storage/
  PolyCopyTrader.Service/
  PolyCopyTrader.Dashboard/

tests/
  PolyCopyTrader.Tests/
```

## Build

```powershell
dotnet build
```

## Test

```powershell
dotnet test
```

## QA Check

Run the repeatable pre-live QA gate before any authenticated/live-trading work:

```powershell
.\scripts\qa-check.ps1
```

Use `.\scripts\qa-check.ps1 -SkipRuntimeSmoke` when another service instance is already bound to the local IPC port.

## Requirement Fidelity Gate

Repository changes use a mandatory, fail-closed requirement contract. Before
material edits, the agent records the user's words verbatim, literal `REQ-*`
items, scope, assumptions, deviations, mapped files, and planned verification;
the user approves it with exact `APPROVE <contract-id> <semantic-digest>` text.
Before completion, a different reviewer compares the request and contract with
the diff and passing evidence.

The full contract is in
[`Codex/Rules/RequirementGate.md`](Codex/Rules/RequirementGate.md). Local Git
hooks can be enabled with:

```powershell
.\scripts\requirements\Install-RequirementGitHooks.ps1
```

Project Codex hooks apply to new tasks after the project and exact hook hash are
trusted in Codex; an existing task must be restarted to rebuild its instruction
chain. Central enforcement additionally requires the
`requirement-contract` GitHub check to be required by default-branch protection.
Preserve the approval and implementation commits when merging; a squash merge
removes the mechanically checked approval-before-edit checkpoint.

## Operations Docs

Operational documents live under `docs/`:

- `docs/runbook.md`
- `docs/incident_response.md`
- `docs/live_trading_checklist.md`
- `docs/paper_trading_evaluation.md`
- `docs/configuration_reference.md`

## Run Service

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

The service runs startup safety checks, the scanner, signal engine, paper/live maintenance engines, heartbeat writer, daily analytics report generator, market WebSocket client, and localhost-only IPC server. It writes rolling logs under its output `logs` directory.

## Local IPC

The service exposes local HTTP endpoints on `Ipc:ListenUrl`, default `http://127.0.0.1:5118/`. The listener refuses non-loopback URLs.

```text
GET  /health
GET  /status
POST /pause
POST /resume
POST /pause-scanning
POST /resume-scanning
POST /pause-paper
POST /resume-paper
POST /pause-live
POST /resume-live
POST /kill-switch
POST /clear-kill-switch
POST /cancel-all-live
POST /refresh-trader-discovery
POST /refresh-onchain
POST /refresh-onchain-markets
POST /cancel-onchain
POST /pin-asset?assetId=...
POST /unpin-asset?assetId=...
```

Dashboard pause/resume, kill-switch, paper-control, trader discovery, on-chain ingestion, on-chain market enrichment, and asset pin/unpin buttons call these endpoints. Commands are recorded in `service_command_audit`.

## Windows Service

Publish the service and install it with Windows Service Control Manager:

```powershell
$gitCommit = git rev-parse --short=12 HEAD
dotnet publish src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -c Release -o .\publish\service -p:SourceRevisionId=$gitCommit -p:InformationalVersion="1.0.0+$gitCommit"
sc.exe create PolyCopyTrader.Service binPath= "$PWD\publish\service\PolyCopyTrader.Service.exe" start= delayed-auto
sc.exe start PolyCopyTrader.Service
```

Use `sc.exe stop PolyCopyTrader.Service` and `sc.exe delete PolyCopyTrader.Service` to stop/remove it. Keep `POLYCOPYTRADER_POSTGRES_CONNECTION` configured as a machine/user environment variable for the service account.
The service writes its deployed build marker to `service_heartbeats.version`.
After restart, verify production is running the expected commit with:

```sql
SELECT service_name, version, started_at_utc, last_heartbeat_utc
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
```

For normal Git-based publishes the value includes `info=1.0.0+<short-git-commit>`.
If a deployment pipeline cannot build from a Git checkout, set
`POLYCOPYTRADER_DEPLOYMENT_VERSION` for the service account to an explicit
artifact id; it will be included as `deploy=<value>`.

Deployment scripts are available under `deploy/`:

```powershell
.\deploy\install-service.ps1
.\deploy\start-service.ps1
.\deploy\stop-service.ps1
.\deploy\backup-db.ps1
.\deploy\uninstall-service.ps1
```

See `deploy/README.md` for VPS security, backup, logging, RDP/firewall, secret handling, and geoblock requirements.

## Print Config Summary

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -- --print-config
```

The summary is sanitized and does not include secrets. Live trading is disabled by configuration and validation. The service config requires PostgreSQL to be configured.

## Run Dashboard

```powershell
dotnet run --project src/PolyCopyTrader.Dashboard/PolyCopyTrader.Dashboard.csproj
```

The dashboard polls PostgreSQL every `Dashboard:RefreshIntervalSeconds`, default `60`, and derives the prominent service-state banner from the selected database's `service_heartbeats` row instead of probing localhost IPC. It evaluates heartbeat staleness against the selected PostgreSQL server clock, so a remote database cannot look healthy just because the dashboard machine clock differs from the VPS. This keeps the banner correct when the top database selector is switched from the configured local PostgreSQL connection to the remote PostgreSQL connection using the same connection string with host `192.168.0.101`. By default `Dashboard:StrategiesOnlyMode=true`, so the refresh reads only the service heartbeat, cumulative strategy performance, short-window strategy performance, and recent Paper/Live orders for the strategy-linked order tabs. Heavy non-strategy tabs and the general IPC command toolbar are hidden in this mode, while the local `Dashboard Errors` tab stays visible so refresh/command/export failures can be copied or saved without loading watchlist, trader discovery, on-chain, market-data, analytics, risk, log, or auth-readiness data. Strategy performance rows are cached separately and refreshed every `Dashboard:StrategyRefreshIntervalSeconds`, default `60`, unless a strategy command invalidates the cache. Recent signal reads first select the requested rows through the descending creation-time index and aggregate rejection codes only for that bounded set. Optional analytics report grids use `Dashboard:OptionalReportTimeoutSeconds`, default `8`, only when `StrategiesOnlyMode=false`; they degrade to an empty grid plus a Diagnostics warning if a heavy remote report query times out. DataGrid row selection is restored across refreshes by stable row keys so horizontal inspection is not interrupted by the refresh cycle. Live skipped strategy columns are split into condition skips, technical/preflight skips, and ignored placed-or-attempted orders that did not work; ignored orders are also broken down into GTD unfilled, cancel/zero-fill, and rejected/error counts so a non-qualifying market window is separated from preflight failures, maker orders that never filled, cancel/reconciliation cases, and failed live submissions. Run-based live condition/GTD skip counts start at `strategies.live_enabled_at_utc`, so enabling Live for a strategy does not inherit its older Paper skip history. If PostgreSQL is not configured, it opens with empty states and a clear storage status. Schema initialization is owned by the service so dashboard startup is not blocked by database migrations or index creation.

Strategy performance is served from the flat `dashboard_strategy_performance_snapshots` and `dashboard_strategy_recent_performance_snapshots` tables. The service performs one initial MVCC-consistent bootstrap, then PostgreSQL outbox triggers record metric-relevant changes from strategies, Paper orders/fills/runs/positions/settlements, and Live orders. High-frequency Paper position marks are coalesced to one pending event per position and applied against a stored position contribution, so repeated WebSocket repricing cannot grow the queue per tick. The projection worker applies at most `Dashboard:ProjectionEventBatchSize` events per transaction, default `250`, to shorten the period for which selected outbox rows remain locked while snapshot writes commit. It continuously expires exact `1h`, `6h`, and `24h` facts through three disjoint partial-index scans without rescanning raw Paper/Live history. A separate reconciliation worker rebuilds exactly one strategy after each `Dashboard:ProjectionReconciliationIntervalSeconds` delay, default `30`; the interval is bounded to `5..3600` seconds. Reconciliation stays single-item, and the repository advisory transaction lock serializes it with the other projection work. The worker disables PostgreSQL parallel workers, uses `4MB` work memory, and limits each reconciliation statement to `15s`. This background pass verifies and repairs drift without putting the old all-strategy aggregate on the Dashboard read path.

## Storage

The service uses PostgreSQL through Npgsql. Do not store credentials in repository files. Configure the connection string through the `POLYCOPYTRADER_POSTGRES_CONNECTION` environment variable.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

`PolyCopyTrader.Service` requires PostgreSQL storage. If no PostgreSQL connection string is configured, the service fails on startup instead of silently using a no-op repository. This keeps Polymarket HTTP logs, API errors, commands, and trading events from disappearing during debugging. The dashboard can still open without storage and will show empty/diagnostic states. `Storage:MaxPoolSize` is set to `64` for the service and `8` for the Dashboard so a burst cannot consume every client slot on the production PostgreSQL server. PostgreSQL sessions use distinct `application_name` values, `PolyCopyTrader.Service` and `PolyCopyTrader.Dashboard`, so server activity can be attributed to the correct process.

Age-based strategy-run retention is fail-closed and disabled by default through both `StrategyRunRetention:Enabled=false` and `StrategyRunRetention:ApplyEnabled=false`. Legacy `strategy_market_paper_runs` remain `Unknown` and are never compacted. New runs are classified monotonically as `PaperOnly` or `LiveOrShadow`; once a strategy is observed with Live enabled, its append-only Live guard protects all later runs. A skipped run at or after the strategy's current `live_enabled_at_utc` is also kept raw even if its retention scope is still `PaperOnly`, because the compact Paper rollup deliberately does not carry run-based Live-skip counters; an older pre-Live skip can still qualify when every other fail-closed check passes. Linked Paper orders, positions (including closed zero positions), settlements, signals, diagnostics, Live orders, Live-shadow decisions, and projection work also block age-based retention. Consequently, complete Paper and Live bet histories remain raw; only a dependency-free terminal Paper-only `Skipped` run with a known market end and both end/update timestamps older than the configured retention window can qualify. The enforced minimum window is 48 hours.

The first-stage direct Paper-skip path has two independent fail-closed gates. After the index, query-plan, candidate/dependency, and retained-history rollout gates were verified, the checked-in service configuration now sets `StrategyRunRetention:DirectPaperSkipCompactionEnabled=true` and `StrategyRunRetention:DirectPaperSkipCompactionApplyEnabled=true`; the typed option defaults remain `false` when configuration is missing. With both gates enabled, a new terminal run that is already proven to be a pure, neutral-fee Paper-only/no-bet `Skipped` run is written atomically straight to the UTC-day/reason rollup and versioned deduplication/restoration tombstone; PostgreSQL emits the same canonical logical Dashboard insert and queues reconciliation without first inserting and deleting the wide raw row. An existing durable `Observed` run that later becomes eligible still follows the raw update-then-archive lifecycle. Any signal/order/entry/settlement field, non-neutral fee metadata, Paper/DryRun/Live order, Live-shadow decision, Paper position/settlement, copied-leader/on-chain dependency, Live retention scope, missing reason, or persisted diagnostics leaves the complete raw row in place. Duplicate run IDs or normalized strategy/market keys in one direct input request abort before any persistence rather than choosing an unordered conflict winner. The existing skipped-run writer normalizes diagnostics to `NULL`; direct compaction does not broaden that pre-existing persistence contract, and the exact skip reason and time remain in the marker. This stage deliberately keeps the durable raw `Observed` queue, so it does not eliminate the initial `Observed` insert or its WAL/index churn.

Compact skipped-run archive v2 uses a two-release compatibility boundary. This compatibility release can install, read, restore, and directly test the dormant v2 representation, but product v2 writes are compiled off through `StrategyRunRetentionCapabilities.CompactSkipArchiveV2ProductWritesSupported=false`. The typed `StrategyRunRetention:CompactSkipArchiveV2Enabled` option defaults to `false`, the checked-in value is `false`, and startup validation rejects `true` even when it comes from an environment-variable override. Therefore this release alone produces no storage savings and product execution continues to emit v1 only. Activation requires a separately approved contract and build plus a durable database capability/version fence that blocks every v1-only service before it can perform schema or data writes; observing only the currently running process is not sufficient.

The checked-in schema installs two narrow partial indexes for the direct path before hosted workers start. `(strategy_id, run_updated_at_utc, archived_run_id)` serves frequent single-strategy Dashboard reconciliation, while `(run_updated_at_utc, strategy_id, archived_run_id)` serves global 24-hour rebuilds; both contain only complete v1 tombstones and are created concurrently. The direct recent-performance reader also repeats its frozen overall `@NowUtc - 24 hours .. @NowUtc` bound inside the archived-tombstone source, in addition to the individual `1h`/`6h`/`24h` window joins, so PostgreSQL can constrain the time-leading index before joining the windows. Deploy and verify this index-only stage before enabling either direct-compaction gate: both indexes must have the exact expected `pg_get_indexdef` and `indisvalid = indisready = indislive = true`, because an interrupted concurrent build can leave a same-name invalid index. The two bulk age-based retention gates remain `false`; both direct-compaction gates are `true` in checked-in service configuration.

Preview-only mode for age-based retention scans at most 500 intrinsic rows per cycle in `(updated_at_utc, id)` keyset order, classifies every dependency for that bounded page, and logs the exact eligible allowlist without changing data. Each sweep freezes its cutoff until it reaches the intrinsic end; its continuation cursor advances by the last inspected intrinsic row even when the whole page is blocked. This makes the normal append-only sweep finite, revisits temporarily blocked rows on the next sweep, and prevents an old blocked prefix from hiding later dependency-free skips. Reaching the end clears both cursor and cutoff so the next cycle starts a fresh sweep. It deliberately does not claim a global eligible total. The separate summary repository operation remains an exact read-only global diagnostic and fails instead of returning a partial result if its 30-second command timeout is exceeded. Eligibility dependencies are evaluated set-wise against the materialized candidate relation, including closed zero Paper positions, while the diagnostic blocker function remains the independent parity oracle in PostgreSQL integration tests. Apply mode must be enabled separately after reviewing the bounded preview. Every batch first takes the exclusive strategy-run retention gate, then opens its serializable transaction and rechecks the exact logged run-ID allowlist through the same blocker pipeline. It adds UTC-day/skip-reason lifetime rollups plus compact versioned tombstones containing the 14 variable fields needed to reconstruct an eligible skipped run, suppresses the matching Dashboard delete events, queues reconciliation, and deletes the same number of raw rows or rolls back. Lifetime Dashboard and direct strategy-performance skip counts include the rollups. Age-based tombstones are outside the recent windows; fresh direct-compaction tombstones supply exact per-run facts for Dashboard `1h`/`6h`/`24h` counts, top reason, and last-run time, while their rollups remain the sole lifetime contribution.

Normal strategy-run and durable dependency writes share the compatible side of the same gate. A successfully inserted or key-updated Paper order, dry-run order, Live order, Live-shadow decision, Paper position/settlement, copied-leader position/activity, or on-chain Paper result therefore cannot pass an overlapping retention batch unnoticed. Strategy-code changes take the transaction-scoped exclusive side because they can make an existing position or settlement newly match a strategy; position/settlement writers take their shared gate before resolving that mutable code. If such a dependency arrives after archival, its `READ COMMITTED` transaction atomically removes the matching versioned tombstone, restores the exact raw run, recomputes or removes the affected rollup, promotes Live/Live-shadow runs to `LiveOrShadow`, suppresses incremental events only for the restored run IDs, and queues a full Dashboard reconciliation. Duplicate `ON CONFLICT DO NOTHING` writes do not restore anything, and any legacy/incomplete tombstone or archive/rollup mismatch aborts the dependency write instead of guessing. Repository write paths use `READ COMMITTED`; a higher-isolation external dependency writer is rejected by this restoration contract.

The two bulk age-based retention switches remain `false`; the two direct-compaction switches are `true` in checked-in service configuration after the required schema, query-plan, candidate/dependency, and retained-history checks. This direct activation does not scan or backfill accumulated raw history: it applies only when a new terminal skip is submitted or an existing durable `Observed` row is finalized as `Skipped`. After deployment, verify exact new tombstone/raw/rollup counts, Dashboard reconciliation, and protected Paper/Live/live-shadow canaries before accepting sustained operation. Any future bulk-retention activation, supporting index, or broader lifecycle change remains separately approval-gated. Existing legacy tombstones cannot be losslessly backfilled after their raw rows have already been removed.

Paper/Live shadow testing stores the shared BTC decision in `paper_live_shadow_decisions`, links `paper_orders` and `live_orders` by `correlation_id`, and writes fatal mismatches to `paper_live_shadow_discrepancies`.
Orders with `execution_source=paper_live_shadow_test` are excluded from ordinary market-data Paper fill simulation. Persisted cumulative Live execution is the sole fill authority: Paper order, canonical cumulative fill, aggregate position cost, and copied-leader size are reconciled idempotently in one wallet-serialized PostgreSQL transaction. Terminal partial fills close as `PartiallyFilledExpired`, and a Paper-projection repair failure cannot block the corresponding Live balance settlement.

Auto-redeem stores claim-ready resolved positions in
`polymarket_auto_redeem_attempts`. It builds standard binary CTF redeem calldata
and can submit Deposit Wallet `WALLET` batches through the Polymarket relayer
when `PolymarketAutoRedeem:AutoSubmitEnabled` is explicitly enabled. Live
submission is throttled by `PolymarketAutoRedeem:MaxLiveSubmissionsPerCycle`,
which defaults to one claim per cycle.

### BTC 5m History Backfill

The service has a one-shot PostgreSQL backfill command for `btc_5m_history`.
It builds `btc-updown-5m-<unix>` slugs over the requested UTC range and loads
closed/resolved BTC Up or Down 5m markets directly from Polymarket Gamma API,
including markets with zero volume. PostgreSQL is used only for truncating,
reading, and writing the output/cache table `btc_5m_history`; the local
`polymarket_gamma_markets` cache is not used as the market source for this
backfill. The command then rebuilds the `(seconds, cents)` counters from public
Binance BTCUSDT 1-second `klines`, using the latest completed 1-second close at
or below each market sample time.

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -- --fill-btc-5m-history
```

For a non-destructive smoke check, add `--btc-5m-history-dry-run`. Useful bounds
for testing are `--btc-5m-history-max-markets <n>`,
`--btc-5m-history-start-utc <iso-utc>`, and
`--btc-5m-history-end-utc <iso-utc>`. Without an explicit start, the command
starts at `2025-12-18T04:25:00Z`, the earliest resolved BTC 5m Gamma market
confirmed during the May 14 API scan. Gamma API batching can be tuned with
`--btc-5m-history-gamma-batch-size <n>` and
`--btc-5m-history-gamma-delay-ms <n>`. This command does not place or cancel
orders and exits before the normal service host starts.

### BTC 5m Statistics Worker

`BtcUpDown5mStatistics` is a disabled read-only research worker. When explicitly
enabled in configuration and by a matching runtime strategy row, it polls the
current Binance BTC/USDT reference while active BTC 5-minute markets are open,
looks up `btc_5m_history` around the current `(seconds, cents)` point with
four-point interpolation, compares the estimated Up/Down probability with the
current Polymarket Up/Down quote, and writes one row per observation to
`btc_up_down_5m_statistics_ticks`. It records decisions such as
`insufficient_history`, `market_price_missing`, `no_positive_edge`,
`up_above_market`, and `down_above_market`; it never creates Paper, dry-run, or
live orders.

The checked-in service configuration currently keeps this worker disabled
(`BtcUpDown5mStatistics:Enabled=false`) so the research tick table does not keep
growing during normal live operation.

Live sampled `(seconds, cents)` points are first stored in
`btc_5m_history_live_observations`. After the market is resolved, the worker
reads the final Up/Down result from closed Gamma metadata and only then
increments `btc_5m_history.count`, `up_count`, and `down_count`. This keeps
unresolved live observations out of the historical outcome counters.

Useful audit queries:

```sql
select decision_code, count(*) as ticks, count(*) filter (where would_bet) as would_bet
from btc_up_down_5m_statistics_ticks
group by decision_code
order by ticks desc;

select applied_to_history, count(*) as observations
from btc_5m_history_live_observations
group by applied_to_history;
```

### Local PostgreSQL Debugging

If PostgreSQL is already installed locally, create a `polycopytrader` database and set the connection string in your shell:

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=127.0.0.1;Port=5432;Database=polycopytrader;Username=postgres;Password=<local-password>;SSL Mode=Disable;Include Error Detail=true"
.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase
```

In a second terminal, use the same connection string for the dashboard:

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=127.0.0.1;Port=5432;Database=polycopytrader;Username=postgres;Password=<local-password>;SSL Mode=Disable;Include Error Detail=true"
.\scripts\run-local-dashboard.ps1 -NoPostgres
```

If PostgreSQL is not installed, use the dev-only Docker Compose file instead. The container binds PostgreSQL to loopback only.

```powershell
.\scripts\start-local-postgres.ps1
.\scripts\run-local-service.ps1 -Mode Paper -RequireDatabase
```

In a second terminal, run the dashboard against the same Docker database:

```powershell
.\scripts\run-local-dashboard.ps1 -NoPostgres
```

Stop the local database without deleting data:

```powershell
.\scripts\stop-local-postgres.ps1
```

Use `.\scripts\stop-local-postgres.ps1 -DeleteData` only when you intentionally want a fresh local database volume.

## Polymarket Public APIs

The `PolyCopyTrader.Polymarket` project contains read-only clients for:

- Data API: trader leaderboard, user trades, current positions, and closed positions.
- Gamma API: active market discovery and market metadata enrichment.
- CLOB public API: order book, server time, midpoint, and spread.
- Geo endpoint: current geoblock status.

User trade calls explicitly send `takerOnly=false` when requested so maker fills are not silently excluded. HTTP failures are retried for transient `429`/`5xx` responses with exponential backoff starting at one second, and persisted to `ApiErrors` through PostgreSQL when retries are exhausted.

Polymarket HTTP diagnostics can be written to PostgreSQL table `polymarket_http_logs`, but the service no longer persists every successful request by default. The default `PolymarketHttpLogging` policy skips successful requests and expected `404` lookups, while retaining network failures, `401/403`, `429`, and `5xx` responses. Optional success logging can be enabled globally or sampled with `SuccessfulRequestSampleRate`. A retention worker deletes successful diagnostics after `SuccessfulRetentionHours` and failed diagnostics after `FailedRetentionDays`. Rows include component, operation, method, request URL, request/response UTC timestamps, duration, attempt number, HTTP status, success flag, response body preview, and error message. Request bodies and auth headers are not stored.

### Gamma Active Markets

The service runs a read-only Gamma active-market ingestion worker when `GammaMarketIngestion:Enabled=true`. Each cycle calls `/markets?active=true&closed=false&limit=500&order=createdAt&ascending=false`, with `offset` incremented for later pages, and upserts rows into `polymarket_gamma_markets`.

Each cycle walks the full active-market result set page by page until Gamma returns an empty array. New `market_id` rows are inserted and existing rows are updated only when Gamma market fields actually change, including order minimum size, price tick size, best bid/ask, spread, last trade price, liquidity, volume, status flags, category, outcomes, and CLOB token ids. Unchanged rows are not rewritten just to move `fetched_at_utc`. The worker then waits `GammaMarketIngestion:PollIntervalSeconds`, default `0`, before starting another full pass.

`CryptoUpDown5mResultPolling` is the read-only BTC/ETH/SOL 5-minute result
collector and latency diagnostic. Every `PollIntervalSeconds` seconds, default
`5`, it selects recently ended local Gamma markets through a bounded end-time
query backed by partial active-market indexes. When provisional order-book
results are enabled, it first checks fresh WebSocket/CLOB `/book` depth for the
ended market: the inferred winner must have best bid at or above
`ProvisionalWinnerBidMin` (`0.60` by default), and the opposite outcome must be
at or below `ProvisionalLoserAskMax` (`0.40` by default) on top-book evidence.
That provisional result is written to the existing
`crypto_up_down_5m_websocket_resolved_markets` ledger with source
`TerminalOrderBook`. The same worker keeps polling the closed-market Gamma
lookup for the concrete slug and later confirms or overwrites the ledger row
with source `GammaClosedMarket` when Gamma returns an unambiguous winner. It
also writes one aggregate row per market to
`crypto_up_down_5m_result_polling_observations`, including polling attempts,
first `closed` time, first winner time, and delay seconds from the 5m window
end.

### BTC Reference Diagnostics

The service keeps the Binance BTC/USDT trade stream as the operational BTC
reference for Middle strategies. When `ChainlinkBtcUsdDiagnostics:Enabled=true`,
an additional diagnostic worker polls Chainlink's BTC/USD Data Streams live-data
endpoint every 10 seconds, pairs the nearest Chainlink benchmark with the latest
fresh Binance trade point, and stores the result in
`btc_usd_reference_correlation_samples`. These rows are for correlation analysis
only and do not influence strategy decisions.

`BtcOrderBookLagDiagnostics` is disabled in the service config by default. When
`BtcOrderBookLagDiagnostics:Enabled=true`, the service stores a
short-retention event-level archive in `btc_order_book_lag_diagnostic_events`.
It records every received Binance BTC/USDT trade, Binance REST `bookTicker`
snapshot, and Polymarket top-of-book WebSocket update with local receive time,
source event time where available, best bid/ask/mid, level sizes where
available, and local lag milliseconds. This archive is meant to test whether
Binance ticks or quote changes lead Polymarket order-book moves; it is buffered
in memory and cleaned by retention so it does not replace the compact odds
archive.

For one-off visual comparison of Binance SBE best bid/ask, Binance JSON
`bookTicker`, and the active BTC 5-minute Polymarket order book, run the service
with `--btc-source-comparison-csv`. This command starts before the normal host,
does not use PostgreSQL, samples one BTC 5-minute market in memory, and writes a
CSV under `artifacts/btc-source-comparison`. The raw BTC/USD prices and
Polymarket probability are different units, so the CSV also contains normalized
from-start bps columns intended for plotting the three sources on one chart.
Binance SBE requires the API key id in `POLYCOPYTRADER_BINANCE_SBE_API_KEY`,
`--binance-sbe-api-key`, or `--binance-sbe-api-key-file`; the Ed25519 private
key file alone is not sent and is not enough for the WebSocket header.

### Prospective BTC, ETH, and SOL Order-Book Prediction Study

`--crypto-orderbook-prediction-study` runs a separate read-only experiment for
the question: can Binance top-of-book state available before a market opens
predict the official outcome of the next Polymarket five-minute BTC, ETH, or SOL
market? Select exactly one asset per process with
`--crypto-orderbook-study-asset btc|eth|sol`. For a market starting at UTC/Unix
boundary `S`, the default decision time is `S - 30 seconds`; every feature uses
only messages whose local monotonic receive time is strictly before that cutoff.
The targets are the final Gamma `Up`/`Down` outcomes for
`btc-updown-5m-<S>`, `eth-updown-5m-<S>`, or `sol-updown-5m-<S>`. Binance
start/end direction is only a diagnostic proxy and is never the canonical label.

Run each asset in its own process and output root. Example 72-hour prospective
ETH and SOL captures using the public JSON stream and no API key:

```powershell
.\PolyCopyTrader.Service.exe `
  --crypto-orderbook-prediction-study `
  --crypto-orderbook-study-mode collect `
  --crypto-orderbook-study-asset eth `
  --crypto-orderbook-study-source json `
  --crypto-orderbook-study-output-dir 'D:\PolyCopyTraderResearch\crypto-orderbook\eth' `
  --crypto-orderbook-study-duration-minutes 4320

.\PolyCopyTrader.Service.exe `
  --crypto-orderbook-prediction-study `
  --crypto-orderbook-study-mode collect `
  --crypto-orderbook-study-asset sol `
  --crypto-orderbook-study-source json `
  --crypto-orderbook-study-output-dir 'D:\PolyCopyTraderResearch\crypto-orderbook\sol' `
  --crypto-orderbook-study-duration-minutes 4320
```

For one aligned BTC/ETH/SOL cohort, use the supervisor script. It still launches
one isolated service process and run directory per asset:

```powershell
.\scripts\run-crypto-orderbook-study-cohort.ps1 `
  -ServiceExecutable 'D:\PolyCopyTraderResearch\runner\PolyCopyTrader.Service.exe' `
  -OutputRoot 'D:\PolyCopyTraderResearch\crypto-orderbook' `
  -ControlRoot 'C:\ProgramData\PolyCopyTrader\OrderBookStudy' `
  -CampaignId 'crypto-orderbook-72h-001' `
  -DurationSeconds 259200
```

For the unattended Windows setup, first validate the exact pinned runner and task
definition without changing the machine:

```powershell
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe `
  -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\install-crypto-orderbook-study-system-task.ps1 `
  -ValidateOnly
```

Then run the same installer from an elevated Windows PowerShell prompt:

```powershell
.\scripts\install-crypto-orderbook-study-system-task.ps1 `
  -StartAfterInstall `
  -DisableLegacyTask
```

The installer verifies both the pinned service SHA-256 and a deterministic
SHA-256 fingerprint covering every file in the publish, then copies the publish
plus the supervisor and watchdog through a verified staging directory into a
versioned, ACL-protected runtime under
`C:\Program Files`, keeps protected campaign control state under
`C:\ProgramData`, and writes the large event archive to a separately protected
directory on `D:`. The main collector task runs as the passwordless, noninteractive
`LOCAL SERVICE` account because all study endpoints are public; this survives
user logoff without granting the collector Local System privileges. A separate
protected `SYSTEM` watchdog checks a compact supervisor heartbeat once per minute
without opening `ControlRoot`, `OutputRoot`, or any archive file. Heartbeats use
the dedicated 64-MB `PolyCopyTrader-OrderBookStudy` Windows Event Log; its channel
ACL grants write access only to `LOCAL SERVICE`, `SYSTEM`, and elevated
administrators, while the local `Users` group receives read access. Task Scheduler
Operational logging is also enabled during installation. The installer
accepts only the documented deployment roots and refuses to replace ACLs on an
unmarked non-empty directory. An unreferenced incomplete versioned runtime is
moved to a recoverable quarantine name before a fresh staged copy is promoted.
Without `-StartAfterInstall`, both new tasks remain
disabled; with it, installation succeeds only after a current campaign heartbeat,
all three first five-minute collecting checkpoints, `LOCAL SERVICE` process
ownership, and a recent successful `SYSTEM` watchdog run are verified. This gate
normally takes a little over five minutes. Only then may `-DisableLegacyTask`
disable the exact old task.

An upgrade or reinstall refuses to replace either protected task while it is
enabled or running. Deliberately disable both protected tasks first. Use
`-ReplaceExistingTasks` only after reviewing a changed definition that still
belongs to the protected runtime and uses the same passwordless service-account
principal; password-backed tasks are never accepted for automatic rollback.

`LOCAL SERVICE` is a least-privilege reliability choice, not an adversarial
isolation boundary from another Windows process already running under the same
built-in SID. The protected runtime prevents ordinary-user code replacement, but
research hashes must still be revalidated before any financial inference.

The supervisor holds a system-awake request, writes atomic cohort and campaign
heartbeats, rejects duplicate campaign instances, and checks each asset's
`events.index.json` freshness only while that child is collecting. Index freshness
is intentionally not applied while Gamma analysis is running. All three child
processes are assigned to one Windows Job Object, so loss of the supervisor closes
the exact process tree instead of leaving orphan collectors. A nonzero child,
invalid identity, missing run, or two consecutive stale-checkpoint observations
fails the whole aligned attempt; Task Scheduler or the external watchdog then
starts a new attempt. Completion validation emits its own progress heartbeat while
hashing the finalized segments, so a correct long validation is not mistaken for
a hung supervisor.

A restart always creates a new isolated cohort and new per-asset output roots such
as `D:\PolyCopyTraderOrderBookStudy\data\btc\cohorts\<cohort-id>\runs`.
The durable campaign
guard prevents a successful 72-hour campaign from starting again after a later
boot. Interrupted fragments are preserved and are never silently resumed, merged,
or presented as one continuous sample. A full attempt is complete only after all
three run manifests, completed indexes with events, index and segment SHA-256
checks, and asset-matched `analysis.json` files exist. The lower-privilege
supervisor performs that full validation and only then publishes a terminal event;
the `SYSTEM` watchdog consumes the protected event and disables both tasks without
traversing lower-privilege-writable data paths. This is not a second independent
archive validation. Campaign completion is terminal: starting a later campaign,
or recovering from archive corruption discovered after completion, requires
rerunning the installer or an explicit manual re-enable. `InsufficientData` is
still a valid analysis result; it is reported rather than silently extending or
pooling the sample.

`WakeToRun` and the runtime power request protect against normal idle sleep, but
cannot prevent an explicit shutdown, forced sleep, reboot, or loss of power. Keep
the machine on AC power for a continuous sample. After a reboot during an
incomplete campaign, the protected boot task starts a new isolated full-duration
attempt; the prior finalized SHA-256-indexed segments remain available as a
separate fragment. After verified completion, both tasks remain disabled.

The output directory must be absolute. The collector writes atomically finalized
and SHA-256-verified gzip segments, with a five-minute rotation by default, plus
`events.index.json` checkpoints. A crash can therefore lose the current partial
segment, not all prior hours. `run.json` records the exact endpoints, build,
asset, cutoffs, feature windows, quality gates, stopwatch anchor, counters, and
final index hash. The event index independently binds the same run id and asset,
and analysis rejects manifest/index/raw-event identity mismatches. Analysis also
writes the raw Gamma market JSON and request URL in
`gamma-labels.json`, feature rows in `windows.csv`, untouched-test predictions,
`analysis.json`, and `report.md`. An interrupted run can be re-read from its
finalized index snapshot:

```powershell
.\PolyCopyTrader.Service.exe `
  --crypto-orderbook-prediction-study `
  --crypto-orderbook-study-mode analyze `
  --crypto-orderbook-study-input-dir 'D:\PolyCopyTraderResearch\crypto-orderbook\eth\<run-id>'
```

The default public source is the official Binance Vision combined
`<ASSET>USDT trade + bookTicker` stream. JSON `bookTicker` has no exchange event
timestamp, so availability is based on the local receive stopwatch. Optional SBE
uses the official `trade + bestBidAsk` stream and accepts only the API key id from
`POLYCOPYTRADER_BINANCE_SBE_API_KEY` or a one-line
`--binance-sbe-api-key-file`; it never reads the Ed25519 private key. Both modes
are restricted to their exact official hosts and reject every wrapper stream or
payload symbol that does not exactly match the selected asset. The legacy
`--btc-orderbook-prediction-study` and `--btc-orderbook-study-*` aliases remain
available for BTC-compatible automation. No database or order-placement path is
used.

Polymarket resolves these contracts from the corresponding Chainlink USD feed,
not Binance Spot. Therefore BTC, ETH, and SOL must be analyzed as three separate
samples with independent quality gates and chronological splits; their rows must
not be pooled into one train/test result. Identical initial settings make the
coverage and point estimates comparable, but different collection periods remain
a limitation.

The primary model is book-only: L1 imbalance, time-weighted imbalance,
microprice offset, imbalance slope, and observed L1 order-flow change. Trade flow
and premarket return are separate diagnostics/baselines. Evaluation uses a
chronological train/validation/test split with an embargo; thresholds are fitted
on train, selected on validation, and scored once on the untouched test segment.
Each run accepts exactly one decision lead so that an earlier-cutoff sample is
never selected using the future availability of a later cutoff. The embargo
covers both the longest feature window and the decision lead, leaving the prior
split's final market resolved before the next split's first feature window.
Defaults require at least 500 commonly valid labeled markets, three UTC days,
and 100 markets of each class before scoring. Missing features or either class in
any split fail closed as `InsufficientData`.

`ExploratoryPointEstimateLiftVsBothBaselines` means that the common-valid subset
of one untouched chronological segment showed higher point accuracy and balanced
accuracy than both the train-majority and descriptive price-momentum rules. It
does not prove statistical persistence or that the book contributes information
beyond momentum. `ExploratoryPointEstimateLiftVsMajorityOnly` means the book rule
beat majority but not a complete momentum rule on both point metrics, and
`NoObservedPointEstimateLift` means it did not beat majority. This first gate is
L1-only and does not model Polymarket execution, fees, depth, slippage, or fill
probability. Any positive result permits only a longer Paper/shadow study with
paired block confidence intervals, an incremental-feature model, and an economic
replay; it never enables Live trading.

### Certificate Pinning

`Polymarket:CertificatePins` can be configured in development or production for the Polymarket HTTP clients and the market WebSocket. Pins are keyed by endpoint host and use `sha256/<base64 SPKI SHA-256>` format. If a host has no configured pin, normal .NET TLS validation is used. If a host has pins, the certificate must match one of them; arbitrary invalid certificates are still rejected.

Example host keys:

```json
"CertificatePins": {
  "data-api.polymarket.com": [ "sha256/<pin>" ],
  "clob.polymarket.com": [ "sha256/<pin>" ],
  "polymarket.com": [ "sha256/<pin>" ],
  "ws-subscriptions-clob.polymarket.com": [ "sha256/<pin>" ]
}
```

To print the current SPKI pins from the machine that will run the service:

```powershell
.\scripts\get-polymarket-certificate-pins.ps1
.\scripts\get-polymarket-certificate-pins.ps1 -AsAppSettings
```

Review `Subject` and `Issuer` before trusting a pin. If the presented certificate is
not a Polymarket certificate, the local network or host is intercepting TLS.
The Dashboard toolbar also has a `Check certificates` button. It first asks the
local Windows Service over loopback IPC to check the service process TLS/pin
configuration; if IPC is unavailable, it clearly falls back to the Dashboard
process check and writes rows to the `Certificates` tab. The check performs TLS
handshakes only and never reads or prints secrets.

## Auth Research

Task 13 added research notes in `docs/auth_signing_plan.md`. Task 14 added native C# L2 HMAC signing, L2 header construction, secret-provider abstraction, and auth readiness reporting under `src/PolyCopyTrader.Polymarket/Auth`. A later safe bootstrap command added L1 `ClobAuth` signing for CLOB L2 API credential derive/create and Windows Credential Manager storage. Task 15 added native C# CLOB V2 order amount conversion, order construction, EIP-712 dry-run signing, redacted payload rendering, and dashboard/storage visibility for dry-run orders. Task 16 added gated live `POST /order`, cancel-one, cancel-all, and order-status polling support.

`PolymarketAuth` config contains provider and lookup names only; secret values must live in environment variables or Windows Credential Manager. To derive or create CLOB L2 API credentials without sending orders, run `.\PolyCopyTrader.Service.exe --bootstrap-polymarket-api-credentials` from the service output directory while Live is disabled. The command prints only redacted status and Credential Manager target names. Use `--auth-readiness-smoke` to validate local L2 HMAC/header construction without sending HTTP requests, `--clob-authenticated-read-smoke` to validate the same credentials against read-only CLOB `GET /trades`, and `--dry-run-signing-smoke` to validate local order EIP-712 signing. `--clob-cancel-all-smoke` calls CLOB `DELETE /cancel-all`; run it only after confirming any open account orders may be cancelled. Dry-run signing may load a private key only through `DryRunPrivateKeyName`; live signing uses `OrderSigningPrivateKeyName`. Missing or mismatched keys fail closed. Test signing uses a deterministic public development key that must never be funded.

## Market WebSocket

When `Bot:UseWebSockets` and `MarketDataWebSocket:Enabled` are true, the service runs a public market WebSocket client against `wss://ws-subscriptions-clob.polymarket.com/ws/market`.

The subscription set is controlled by `MarketDataWebSocket:SubscriptionScope`. `AllActiveMarkets` subscribes to all active Gamma markets discovered by `GammaMarketIngestion`. The current service config uses `CryptoUpDown5mOnly`, which still upserts every active Gamma market to PostgreSQL but registers only BTC/ETH/SOL Up/Down 5m markets in the WebSocket subscription registry. `BtcUpDown5mOnly` remains available for BTC-only runs. For each registered subscription market, the service updates an in-memory `assetId -> market snapshot` cache before writing the page to PostgreSQL. The snapshot keeps the compact decision-relevant fields: market ids/slugs/title, event/category context, outcome mapping, active/closed/archived/restricted/order-book flags, liquidity/volume, best bid/ask, spread, last trade price, order minimum size, price tick size, and relevant timestamps. It intentionally does not keep the full Gamma raw JSON or long description in memory.

New token ids are subscribed through `assets_ids` as soon as the WebSocket supervisor observes them, and reconnects resubscribe to the current in-memory set. After a full Gamma scan reaches the empty page, token ids missing from the latest `active=true&closed=false` result are removed from the in-memory cache and unsubscribed. WebSocket `book`, `price_change`, `best_bid_ask`, and `last_trade_price` messages update cached bid/ask/last-trade fields on the fly; `price_change` deltas are applied to the last full `book` snapshot so known depth is not replaced by top-of-book-only updates. `market_resolved` removes the resolved asset from the active subscription cache as an early lifecycle hint and writes BTC/ETH/SOL 5-minute Up/Down results to `crypto_up_down_5m_websocket_resolved_markets`. Every raw WebSocket `market_resolved` event is also appended to `market_resolved_event_diagnostics`, including events whose asset id no longer has an active in-memory snapshot, so production checks can distinguish missing exchange events from local snapshot-matching failures. The dedicated critical shard writes parse failures, heartbeats, resolution frames, bulk frames of at least `100` updates, and every `MarketDataWebSocket:CriticalFrameDiagnosticSampleEvery` ordinary frame to `market_websocket_frame_diagnostics`; the default routine sampling interval is `100`, and `0` retains only important frames. Each row contains frame kind, payload hash, extracted event types/assets/markets, parser status, resolved-text flags, and the raw payload truncated to 64KB. Diagnostic rows are persisted by a background queue instead of the socket receive loop.

The WebSocket client also keeps supporting operational subscriptions for:

- open paper-order asset ids;
- open paper-position asset ids;
- recent accepted/high-score signal asset ids;
- asset ids pinned through config or dashboard IPC.

`MarketDataWebSocket:MaxSubscribedAssets=0` means no local cap; prefer `SubscriptionScope` for semantic narrowing because a numeric cap can exclude the BTC/ETH/SOL assets the strategy needs. Closed zero-size Paper positions are retained in PostgreSQL history but are excluded from operational subscriptions; only positive-size positions contribute asset ids. The WebSocket supervisor shards the desired asset ids across multiple `ClientWebSocket` connections instead of using one huge all-active subscription. BTC/ETH/SOL 5-minute Up/Down asset ids from the active registry are isolated into the dedicated `PolymarketMarketWebSocket:crypto-updown-5m-critical` shard before the remaining operational assets are allocated, so Diff result capture does not depend on old paper-position assets sharing a huge shard. `MarketDataWebSocket:ShardMaxAssets` defaults to `3000`, `MaxShardConnections` defaults to `64`, and all outcomes for the same market/condition are kept on the same shard. Shard assignment is stable while the Gamma full scan is still discovering pages: new token ids are sent to existing shard connections with dynamic subscribe messages when there is capacity, instead of restarting all existing shards on every page. Each shard sends `PING` heartbeats, reconnects with exponential backoff, and resubscribes after reconnect. Repeated connect/close flaps without an accepted market update continue increasing the delay up to `MarketDataWebSocket:ReconnectMaxDelaySeconds`; the first update accepted by the side-effect queue (`Enqueued` or `Coalesced`) resets it to `ReconnectBaseDelaySeconds`. Malformed JSON, `PING`/`PONG`, zero-update payloads, rejected/dropped updates, and dispatch failures do not reset it. Subscription messages are sent in `MarketDataWebSocket:SubscriptionBatchSize` chunks to avoid huge single payloads. Frame handling updates only the in-memory registry/cache and enqueues side effects. It logs every bulk frame of at least `100` updates and warns when dispatch itself takes at least one second, including queued/coalesced/failure, payload-size, and duration fields.

The side-effect worker serializes Paper fills, resolution recording, optional raw persistence, and trade diagnostics outside the receive loop. Replaceable quote updates without open Paper orders are coalesced per asset to their latest bid; `last_trade_price`, resolutions, unknown/uninitialized exposure state, and updates for assets with open Paper orders are never coalesced. Enabling `MarketDataWebSocket:PersistMarketDataEvents` or `MarketDataWebSocket:PersistOrderBookSnapshots` disables quote coalescing. `MarketDataWebSocket:SideEffectMaxPendingUpdatesPerAsset`, default `32`, is a soft bound for replaceable work: non-replaceable events are retained even when the bound is exceeded. `MarketDataWebSocket:SideEffectDiagnosticQueueCapacity`, default `256`, may discard routine sampled frames when full but retains parse/API errors and other important diagnostics. Queue metrics are logged every `MarketDataWebSocket:SideEffectMetricsIntervalSeconds`, default `30`; queue delay or processing above `MarketDataWebSocket:SideEffectSlowProcessingMilliseconds`, default `1000`, produces a warning. On service shutdown the WebSocket stops first and the queue drains accepted work.

The supervisor checks shards every `MarketDataWebSocket:WatchdogIntervalSeconds`, default `10`. A shard with a failed receive/heartbeat loop reconnects itself; a still-open shard that has not received any protocol frame for `WatchdogStaleSeconds`, default `90`, is reopened by the supervisor. The aggregate status is stored as `PolymarketMarketWebSocket` in `market_data_status`; individual rows are stored as `PolymarketMarketWebSocket:shard-001`, `PolymarketMarketWebSocket:shard-002`, and so on.

Shard disconnect diagnostics record the close/exception phase, connection attempt, reconnect count before increment, subscribed-asset count, socket/close status, endpoint host, connection age, last-message age, exception type, WebSocket/native error codes, and HResult. The diagnostic text written to logs, status errors, and `api_errors` does not contain endpoint userinfo/query text, raw exception messages, inner-exception messages, or close descriptions; sensitive free text is represented only by its character count and SHA-256 fingerprint so repeated failures can still be correlated. A peer close frame updates the reconnecting status and warning log without creating an API-error row, while an actual connection-loop exception keeps the existing `ConnectionLoop` API-error path. A failed or cancelled frame handler does not reset the reconnect backoff, and a cancelled reconnect delay does not advance it.

For all-active-market monitoring, the high-volume book/price/bid-ask stream is kept in memory by default instead of synchronously writing every update to PostgreSQL. `MarketDataWebSocket:PersistMarketDataEvents` and `MarketDataWebSocket:PersistOrderBookSnapshots` default to `false`; enable them only for intentionally narrow subscription sets. Connection status is still persisted to `market_data_status` with `StatusPersistIntervalSeconds` throttling, default `60` seconds.

Diagnostic trade ticks are controlled by `MarketTradeDiagnostics` and are
disabled by default. When enabled, every `last_trade_price` WebSocket message is
inserted into `polymarket_websocket_trade_ticks` without trader lookup. The row stores
raw JSON, asset/condition ids, side, price, size, trade timestamp, whether
`transaction_hash` was present, and `trader_match_status=1` (`NotFound`).
`trader_wallet`, match timestamps, and match attempts are left empty/zero in the
current diagnostic mode. The previous Data API `/trades?market=...` lookup
helpers remain in code for a later implementation, but the service no longer
runs a queue, pending retry scan, or background wallet enrichment for these
ticks. Market cache updates from WebSocket book/price/bid-ask/last-trade
messages still run normally.

## Data API Trader Activity

The service also runs read-only Data API trader-activity workers when
`DataApiTraderIngestion:Enabled=true`. The discovery worker calls global
`/trades?limit=1000&timestamp=<unix_ms>` with no successful-cycle pause by
default, extracts unique `proxyWallet` values, immediately upserts trader rows,
and then moves on. It does not write global trade rows and does not wait for
per-wallet history, rating refresh, or Gamma enrichment before polling the next
global page; the fast loop only discovers trader wallets. Existing trader rows
are not rewritten on every repeated global page: profile/new-trade changes write
immediately, while seen-only timestamp refreshes are throttled. The trader table
also keeps Polymarket-only rating refresh cursors:
`polymarket_rating_refreshed_at_utc`, `polymarket_rating_next_refresh_at_utc`,
`polymarket_rating_refresh_attempts`, and `polymarket_rating_last_error`.

A separate sync worker selects a small batch of pending or stale traders from
`polymarket_data_api_traders`. For a newly seen wallet, it reads the accessible
per-wallet activity window through `/trades?user=<wallet>&limit=1000&offset=...`,
up to `DataApiTraderIngestion:MaxUserHistoricalOffset`, default `3000`. For an
already known wallet, it reads fresh pages from newest to oldest and stops at the
first trade at or before the wallet's stored `last_trade_timestamp_utc`. Completed
traders become eligible for another fresh sync after
`DataApiTraderIngestion:ExistingTraderRefreshIntervalSeconds`, default `3600`.
The sync worker uses these pages only to advance the wallet cursor and does not
store raw per-wallet trade history in PostgreSQL.

A separate Polymarket-only rating worker continuously selects the oldest due
wallets from `polymarket_data_api_traders` and refreshes
`polymarket_data_api_wallet_category_ratings`. For each enabled
`polymarket_category_mappings` row, it calls `/v1/leaderboard` with
`user=<wallet>`, mapped Polymarket category, configured time period, and
configured ordering. When `PolymarketRatingPositionsEnabled=true`, the same
refresh also reads configured pages from `/positions` and `/closed-positions`,
maps those positions to the same local categories, and stores aggregate current,
closed, and combined position PnL/value/percent fields beside the leaderboard
fields. Leaderboard rows also include `leaderboard_pnl_to_volume_pct`, a derived
`pnl / vol * 100` efficiency ratio; it is not Polymarket's official ROI or
percent PnL. The simplified worker does not store raw per-position rows; the
position columns are a snapshot from the fetched pages. Successful refreshes move
`polymarket_rating_next_refresh_at_utc` forward; failures are logged, recorded in
`api_errors`, and retried after `PolymarketRatingFailureDelaySeconds`.

The older self-computed position/performance path is intentionally disabled in
the processor and left in source as commented legacy logic. If we later need it,
it can again read `/positions` and `/closed-positions`, store
`polymarket_data_api_positions`, and materialize
`polymarket_data_api_wallet_performance` plus
`polymarket_data_api_wallet_category_performance`.

This worker intentionally accepts the known Data API gaps and page jumps. It is
not connected to `leader_trades`, signal generation, paper trading, or live
trading.

## Watchlist Scanner

The service scans enabled `Watchlist:Traders` entries on `Bot:PollIntervalSeconds`. Each enabled wallet is validated before any API call. Recent trades are fetched with `takerOnly=false`, deduplicated, persisted to `LeaderTrades`, and queued as in-memory candidates for the future signal engine. Current positions are written as snapshots to `LeaderPositions`.

Scanner health is persisted to `scanner_status` with last success/error timestamps and per-loop fetched/stored counts. Invalid placeholder wallets are warned and skipped without crashing the service.

## Trader Discovery

Trader discovery is operator-triggered from the dashboard. When `TraderDiscovery:Enabled=true`, the dashboard `Find traders` button asks the service to fetch the full configured Polymarket leaderboard window twice: `orderBy=PNL` for successful traders and `orderBy=VOL` for high-volume loss candidates. Current merged leaderboard rows are stored in `trader_leaderboard_snapshots`, one row per `category + time_period + wallet`, with separate PNL and volume-leaderboard columns. The best PnL candidates and the worst negative-PnL volume candidates are enriched with all-time leaderboard PnL/volume for the same wallet, recent trades, and current positions, then stored in `trader_discovery_candidates`.

Run the service and click `Find traders` in the dashboard controls:

```powershell
.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase
```

The dashboard shows refreshed shortlist rows in the Trader Discovery tab. The `PnL`/`Volume` columns are for the configured discovery period, while `All PnL`/`All Volume` are all-time sanity-check metrics fetched by wallet. Use this only for candidate research; a high leaderboard PnL is not enough to add a wallet to the watchlist without paper evaluation.

## On-Chain Discovery


The older on-chain collection and derived-data workers are currently paused by default. `OnChainIngestion:Enabled` and the older on-chain background flags are set to `false`, and the older hosted-service registrations in `PolyCopyTrader.Service/Program.cs` are commented out. The diagnostic trade-capture worker is registered independently and is controlled by `OnChainIngestion:TradeCaptureEnabled`. Existing PostgreSQL data is not deleted. To resume the older full collection/processing path, restore those registrations and set the required on-chain flags back to `true`.

Run the service to start background ingestion. Click `Onchain sync` in the dashboard controls, or call this endpoint, only when you want to force a manual cycle:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:5118/refresh-onchain
```

Use `Cancel onchain` or `POST /cancel-onchain` to stop the current ingestion run. If background sync remains enabled, the worker will retry on its next cycle. Progress is checkpointed after every completed block batch and repeated batches are idempotent. In `polymarket_onchain_ingest_cursors`, `to_block` is the newest completed block and `from_block` is the oldest completed block currently retained for that contract. On the next run the service scans only `to_block + 1` through the latest Polygon block. It does not scan backward from `from_block - 1`.

Set `POLYCOPYTRADER_POLYGON_RPC_URL` if you want to use a private Polygon RPC provider. Do not commit RPC URLs containing tokens. The default public RPC is only for short manual testing; if it returns pruned-history or rate-limit errors, use a full/archive provider. The diagnostic capture worker scans the configured V1/V2 CTF Exchange and Neg Risk CTF Exchange contracts with `eth_getLogs`, defaults to `TradeCaptureConfirmations=0` for lowest latency, starts from the last `TradeCaptureStartLookbackBlocks` blocks when no cursor exists, and retries RPC errors with exponential backoff from `TradeCaptureErrorDelayMilliseconds` to `TradeCaptureMaxErrorDelayMilliseconds`. The older full ingestion path scans the same contracts, temporarily persists raw logs to `polymarket_onchain_logs`, persists decoded fills to `polymarket_onchain_fills`, normalizes maker/taker rows to `polymarket_onchain_wallet_fills`, aggregates wallet-level tx rows to `polymarket_onchain_wallet_executions`, writes indexed serving rows to `polymarket_onchain_trade_details`, and stores cursors in `polymarket_onchain_ingest_cursors`. Raw log rows are deleted after the decoded fill has been materialized into the indexed serving layer; decoded fills remain the rebuild/audit source.

When `OnChainIngestion:PaperSignalEnabled` is true and Paper runtime is enabled (`Bot:Mode=Paper`, or `Bot:Mode=Live` with `PaperTrading:RunInLiveMode=true`), decoded `OrderFilled` captures can be evaluated immediately inside the trade-capture loop. With the current low-latency service config, `TradeCapturePersistCaptures=false`, `PaperSignalBacklogEnabled=false`, `PaperSignalHotPathEnabled=true`, `TradeCaptureSkipStaleCursor=true`, `PaperSignalHotMaxAgeSeconds=2`, and `PaperSignalLatestCandidatesLimit=100`, so PostgreSQL keeps only the per-contract capture cursor while fresh captures are resolved from memory into paper-signal candidates. The older backlog worker can still be re-enabled for diagnostics by turning `PaperSignalBacklogEnabled` back on and persisting captures. The hot path keeps only the latest configured capture window, resolves it through `polymarket_gamma_markets`, `polymarket_category_mappings`, and `polymarket_data_api_wallet_category_ratings`, drops SELL participants from trading selection, pre-scores BUY candidates cheaply, and attempts the sorted BUY candidates until one creates an order or a non-orderbook rejection stops the batch. For this low-latency path, fresh public market WebSocket order books are preferred, but a missing, stale, unsubscribed, or unusable in-memory book triggers an immediate CLOB `/book` request; the response updates the in-memory book cache and the final decision uses that fresh REST snapshot. A candidate is rejected with a `missing_orderbook_rest_*` or empty-side reason only if `/book` is unavailable or unusable, and the next best candidate can then be tried. Paper/live exposure is read from an in-memory snapshot cache that is refreshed from PostgreSQL on first use and updated after paper/live order and position changes. Accepted on-chain Paper BUYs write the signal, paper order, copied-leader link, and on-chain result in one PostgreSQL transaction. The timing log records RPC fetch, decode, hot-signal, persistence, candidate lookup, selection, processing, order-book, exposure, evaluation, and total milliseconds so the candidate window can be reduced if it starts lagging. A selected BUY opens or adds to a copied-wallet paper position and creates a `paper_copied_leader_positions` link after the entry paper order is created. Direct on-chain SELL notifications are not copied; copied exits are handled by the separate leader activity worker. With `PaperTrading:UseMinimumMarketOrderSize=true`, proposed on-chain BUY paper orders use the market `min_order_size`.

For analyst-friendly querying, schema initialization creates two indexed serving tables. `polymarket_onchain_trade_details` is incrementally upserted from decoded fills plus token metadata and exposes maker, taker, maker/taker side, asset amounts, price, size, notional, fee, block time, tx hash, market, outcome, category, and resolved status. `polymarket_onchain_participant_details` is incrementally refreshed from materialized wallet activity, positions, and performance into one participant row per wallet with executions, buy/sell counts, markets, volume, fees, position counts, exposure, resolved PnL, ROI, win rate, score, and first/last trade time.

When `OnChainIngestion:BackgroundMarketEnrichmentEnabled` is true, a second background worker checks queued missing or incomplete on-chain token metadata every `OnChainIngestion:MarketEnrichmentIntervalSeconds`, default `120`, and enriches it through the Gamma API. Click `Enrich markets` or call `POST /refresh-onchain-markets` only when you want to force a manual enrichment cycle. Ingestion and derived-data rebuilds add affected token ids to `polymarket_onchain_token_metadata_refresh_queue`, so enrichment reads a small queue instead of scanning the full wallet-execution table. This fills `polymarket_onchain_token_metadata` with token id, condition id, market slug/title, outcome, category, end date, active/closed/archive status, winning outcome when inferable from outcome prices, and the raw Gamma JSON. Metadata rows with failed lookup or a blank category are retried with a short backoff, and category parsing falls back from `market.category` to nested event/category fields when Gamma omits the top-level category. If token lookup returns metadata without a category, enrichment first fetches the linked Gamma event and derives a category from event category/tags/text; if that still fails, it resolves the parent market through CLOB `markets-by-token/{token_id}` and retries Gamma by `condition_ids`. Each enrichment run processes repeated batches of `OnChainIngestion:MarketEnrichmentBatchSize`, default `100`, until no queued due tokens remain or `OnChainIngestion:MarketEnrichmentMaxBatchesPerRun`, default `25`, is reached.

The on-chain background workers catch transient failures, write `api_errors`, pause, and retry with exponential backoff from `OnChainIngestion:BackgroundErrorDelaySeconds`, default `60`, up to `OnChainIngestion:BackgroundMaxErrorDelaySeconds`, default `900`. Manual commands and background workers share single-run guards; if one is already active, another request returns an already-running message instead of starting duplicate work. Activity, position, wallet-performance, and wallet/category-performance refresh cycles also share a non-blocking PostgreSQL advisory lock, so one derived refresh cycle runs at a time instead of overlapping transactions against the same materialized tables.

When `OnChainIngestion:BackgroundSignalCandidateRefreshEnabled` is true, another background worker converts on-chain wallet fills into `polymarket_onchain_signal_candidates` and `polymarket_onchain_signal_candidate_reasons`. This is a read-only behavior-evidence layer for selecting trusted `(wallet, category)` pairs, not order placement and not one-for-one copy of a current trade. Each row represents one maker or taker wallet side from `polymarket_onchain_wallet_fills`, enriched with token metadata, category, market status, notional, wallet/category performance, score, ROI, win rate, and sample quality. BUY and SELL fills are both retained because exits are part of wallet behavior. Historical market state fields (`active`, `closed`, `archived`, `resolved`) are stored for audit and filtering but do not reject evidence rows; closed/resolved markets are often the rows that prove performance. Rows are marked `Accepted` when market/category metadata is known and the wallet/category performance passes the configured sample, score, ROI, and win-rate gates. Candidate preparation keeps all notional sizes; `Execution:MinLeaderTradeUsd` is not used by this on-chain preparation layer. Otherwise the table records `Rejected` plus explicit reason codes such as missing category, missing performance, or weak score. The worker uses `polymarket_onchain_signal_candidate_refresh_queue` and `polymarket_onchain_signal_candidate_backfill_cursors` to process the full downloaded wallet-fill history in bounded batches (`SignalCandidateQueueSeedBatchSize`, default `1000`; `SignalCandidateBatchSize`, default `250`) and then keep processing new rows as ingestion adds them. Temporary rejections caused by missing metadata/category/performance are requeued in small retry batches (`SignalCandidateRetryBatchSize`, default `250`) instead of rescanning the whole table. Existing rows previously rejected only as `leader_trade_too_small`, `unsupported_side`, `market_inactive`, or `market_resolved` are also requeued so they can be recalculated under the current behavior-evidence policy.

The dashboard `Onchain Trades` tab reads `polymarket_onchain_trade_details` for recent enriched raw fills, and `Onchain Participants` reads `polymarket_onchain_participant_details` for one-row-per-wallet participant summaries. `Onchain Rankings` remains activity-based over materialized wallet activity: execution count, buy/sell counts, distinct token ids, notional volume, maker-side collateral-denominated fees, and a simple activity score. A background activity refresh worker keeps `polymarket_onchain_wallet_activity` updated from a wallet queue so the dashboard does not group the full execution table during every refresh. The `Onchain Positions` tab reads the materialized table `polymarket_onchain_wallet_positions`, which aggregates executions by wallet, token, market, and outcome with buy/sell shares, net shares, net cost, average buy/sell prices, volume, and resolved PnL when Gamma metadata identifies the winning outcome. A background position refresh worker keeps this table updated from a token queue populated by ingestion, derived-data rebuilds, Gamma enrichment, and an initial missing-token seed. `Onchain Leaders` reads `polymarket_onchain_wallet_performance`, a second materialized table refreshed from affected wallets. It combines resolved PnL, ROI, win rate, resolved sample size, volume, and open exposure into a transparent first-pass score. `polymarket_onchain_wallet_category_performance` stores the same style of score per `(wallet, category)`, maintained from a wallet/category refresh queue whenever position refreshes add, remove, or recategorize affected positions. The decoded fill table remains the audit/rebuild layer; the wallet tables, trade/participant detail tables, activity table, positions table, performance table, and category performance table are the fast research layer. If raw fills already existed before the serving tables were added, the next on-chain sync rebuilds missing indexed rows from the stored raw fills without re-reading Polygon RPC.

## Signal And Risk Engines

Queued leader trades are evaluated by `DefaultSignalEngine` after the scanner stores them. The service resolves market metadata from `polymarket_onchain_token_metadata`, loads the leader's row from `polymarket_onchain_wallet_category_performance` for the same market category, loads our local `paper_copied_trader_performance` rows for the copied wallet overall and category, and passes all of that into the signal engine. Low-latency on-chain paper BUY signals use the same engine but source the leader trade from the freshly decoded `OrderFilled` capture and use the Polymarket-only wallet/category rating row as the performance gate. Direct on-chain SELL notifications bypass the signal engine and are ignored because copied exits are tracked from leader Data API activity. With the default service config, the engine rejects unsupported sides, stale trades, leader trades below `Execution:MinLeaderTradeUsd` (default `$0.10`), missing/wide order books, invalid leader prices, unknown categories, missing or weak leader category performance, weak local copied-leader Paper performance, category mismatches, markets too close to event end, and SELL signals without an existing copied-wallet paper position. The local copied-leader guard ignores thin samples, then dynamically blocks wallets/categories after the configured settled-position sample when our total copied PnL, ROI, or bounded 0-100 local score falls below the configured thresholds. Paper-runtime decisions can create proposed paper orders; live placement is a separate preflight path and remains independently gated.

`DefaultRiskEngine` enforces configured bankroll limits for trade, market, trader, category, total deployed exposure, daily loss, and max open orders. The opposite-outcome open-order guard is applied only to Live preflight, using open Live orders in the same market; Paper entries are allowed to record both sides for strategy-effectiveness testing. Rejected decisions are persisted as `SignalRejection` reason codes.

## Strategies

The service startup is currently in an Up/Down strategy worker mode. In `src/PolyCopyTrader.Service/Program.cs`, BTC strategy execution plus ETH/SOL Binance reference and crypto odds archive workers are enabled. Hosted-service registrations that are not needed for this strategy mode remain commented out rather than deleted: HTTP-log retention, Data API trader ingestion/sync/rating, low-latency on-chain Follow leader capture/signals, copied-trader accounting, leader exits, and daily reports. `BotWorker` still writes the service heartbeat, but its watchlist scan and queued Follow leader signal-processing block is also commented out. To resume those tasks, restore the commented registrations and the commented `BotWorker` block.

The BTC `More` comparison variants, including capped and Gamma rows, have been removed from the active seed set and their local/server history was purged.

Trading strategies are stored in PostgreSQL table `strategies`. Built-in rows include the remaining BTC 5-minute family: fixed Up/Down bps Instant, Diff Instant, Diff Premarket, retained Diff Progress thresholds, retained Diff Real Limit Progress Premarket rows, Diff Reference Average Premarket, AdjustedDiff Instant, ShiftDiff Instant, Reference Average bps Premarket rows, and Futures Basis bps Premarket rows. It also includes retained ETH/SOL variants and the ordinary non-Progress families. The staged 217-strategy retirement release excludes the exact approved negative-Gross Progress variants from every runtime catalog and static/dynamic seed path, but intentionally leaves their stopped database rows and structured history untouched until the separately executed history-cleanup step. Three referenced BTC source variants remain stopped and catalogued—Shift Premarket `N=3` and Real Limit Premarket `N=4,5`—because their retained LowerEnter clones still point to them. Shared Progress algorithms remain for surviving strategies. Paper, dry-run, and live order rows carry `strategy_id`, so retained variants can run side by side and be compared by their order/fill outcomes. The dashboard `Strategies` tab aggregates one row per strategy from `strategies`, `paper_orders`, open positions, settlements, strategy run lifecycle rows, and `live_orders`; its filters, runtime controls, Paper mark-to-market fields, recent windows, and separate Live outcome metrics continue to operate on the retained catalog.

The BTC and SOL catalogs each additionally contain 30 Optimized Average Premarket rows for `N=1..10`: 10 Up-trigger, 10 Down-trigger, and 10 neutral rows, grouped in their dedicated Optimized categories.

The catalog also seeds `126` Confirmed Average Premarket rows. The `84` `BTC/ETH/SOL Up or Down 5m N bps Confirmed Average Premarket` rows retain each neutral Bps Reference Average threshold and require agreement with Diff Reference Average `M`, where `M` is BTC `5`, ETH `3`, and SOL `1`. The `42` `BTC/ETH/SOL Up or Down 5m K Diff Confirmed Average Premarket` rows retain each Diff threshold and require agreement with neutral Bps Reference Average `L`, where `L` is BTC `45`, ETH `5`, and SOL `35`. New rows are initially inserted with `Enabled=true`, `Live=false`, and `Paused=false`; later schema starts preserve their runtime flags. A confirming signal is evaluated from its exact linked catalog variant and does not depend on whether that linked strategy row is enabled, paused, or Live.

The catalog seeds `144` Optimized Average Premarket rows. The ETH grid contains 28 Up-trigger, 28 Down-trigger, and 28 neutral thresholds (`1..10`, then `15..100` step `5`); the BTC and SOL grids each contain 10 Up-trigger, 10 Down-trigger, and 10 neutral thresholds (`1..10`). They preserve the ordinary eight-window Max/Min envelope selector, its longer-window tie-break, threshold, and outcome logic. Every configured window with usable data participates whether complete or incomplete, and gaps or incomplete coverage alone do not block the decision. After the ordinary threshold passes, they enter only when the direction-relevant selected boundary uses `3h`: the maximum boundary for an Up trigger and the minimum boundary for a Down trigger. Selecting any of the other seven windows skips with `optimized_average_required_window_not_selected`. The rows are enabled for Paper on first insert, are hard-blocked from every Live path even if the database `Live` flag is later toggled, and are excluded from Child/Child ROI parent selection so the experiment does not alter existing mirror strategies.

The BTC catalog also seeds `324` `LowerEnter` Premarket clones for every currently unserved BTC 5-minute Premarket source strategy: `318` Regular and `6` Progress rows. Each clone preserves its source strategy's signal, direction, timing, linked confirmation inputs, progression state rules, and stake calculation, but submits its simulated BUY FAK with a hard maximum order price of `0.50`. Paper consumes only immediately executable asks at or below that price and cancels the remainder, matching the order semantics available to Live FAK. The clones have stable source mappings, independent strategy IDs and progression state, are grouped into dedicated `LowerEnter` Dashboard categories, and are always Paper-only. They cannot submit Live or Live-shadow orders and cannot be selected as Child or Child ROI parents. The existing `LowEnter Average Premarket` neutral Reference Average grid remains a separate already-covered family and is not duplicated by these clones.

The `Hide progress` checkbox in each `Strategies` tab hides rows whose visible strategy name contains `Progress`.
Each `Strategies` tab has an independent currency filter; selecting BTC, ETH, or SOL narrows both the visible strategy rows and that tab's category dropdown to categories present for the selected currency.

The catalog seeds `360` `BTC/ETH/SOL Up or Down 5m Hh N bps Absolute Premarket` rows: `H=1..24` and `N=1..5` without gaps. They are grouped into separate `BTC`, `ETH`, and `SOL` `Absolute Premarket` Dashboard categories. New rows are inserted with `Enabled=true`, `Live=false`, and `Paused=false`; later schema starts preserve those runtime flags.

Absolute Premarket variants run 30 seconds before market open. They read the persisted Binance reference-price extrema snapshot for the previous `H` hours before fetching the fresh decision price, so that fresh price is not part of its own comparison window. The strategy requires history covering the full window while tolerating isolated missing 10-second ticks. A current price at least `N` bps above the historical maximum buys `Down`; a current price at least `N` bps below the historical minimum buys `Up`; otherwise the run is skipped. Paper and Live-shadow entries use the same guaranteed-worst-price FAK ask-depth path as Reference Average Premarket. The existing `CryptoReferencePriceHistory` worker feeds both caches: average windows keep their proportional bucket averages, while the extrema cache retains exact 10-second observations for every whole-hour window from `1h` through `24h` and uses proportional buckets only to verify history coverage.

Fixed-outcome `Up/Down N bps Instant` variants appear in dedicated `Up Bps` and `Down Bps` Dashboard strategy categories per asset and interval instead of falling into `Other`. Reference-average bps Premarket variants appear in separate `Up Bps Reference Average Premarket`, `Bps Reference Average Premarket`, and `Down Bps Reference Average Premarket` categories per asset. ETH Optimized Average rows similarly use separate Up, neutral, and Down `Bps Optimized Average Premarket` categories. Bps Confirmed Average and Diff Confirmed Average rows use separate per-asset Dashboard categories. Futures Basis bps Premarket variants appear in separate `BTC/ETH/SOL Up or Down 5m Bps Futures Basis Premarket` and `BTC/ETH/SOL Up or Down 5m Bps Futures Basis Revert Premarket` categories. Diff Instant variants appear in `Diff Up` and `Diff Down` categories per asset; Diff Progress variants share one `Diff Progress` category per asset; Diff Shift Progress, Diff Limit Progress Premarket, and Diff Real Limit Progress Premarket variants use separate BTC/ETH/SOL Dashboard categories; Diff Reference Average Premarket variants are split into separate `BTC/ETH/SOL Up or Down 5m Diff Reference Average Premarket` categories; AdjustedDiff Instant variants appear in `AdjustedDiff Up` and `AdjustedDiff Down`; ShiftDiff variants appear by asset and shift. More, old Revert families, ETH Down filtered-average Premarket variants, and Simple strategy rows are no longer seeded and should not appear in Dashboard strategy lists.

Current Premarket seed rows for BTC/ETH/SOL are the reference-average `Up/Down N bps Reference Average Premarket` family plus neutral `N bps Reference Average Premarket` rows with thresholds `1..10` and `15..100` step `5`; they are grouped into separate Up, neutral, and Down Dashboard categories per asset. The former ETH filtered Down reference-average clones named `ETH Up or Down 5m Down N bps Filtered Average Premarket` for `N=1..10` have been removed and are purged by schema initialization. BTC/ETH/SOL also seed `N Diff Reference Average Premarket` rows with thresholds `1..10`, `15`, `20`, `25`, and `30`; those rows are grouped into separate Dashboard categories per asset. BTC/ETH/SOL Futures Basis Premarket and Futures Basis Revert Premarket rows use thresholds `1`, `2`, `3`, `5`, `8`, `10`, `15`, and `20` bps and are grouped into separate Futures Basis categories per asset. The seed also includes `Up/Down 1..10 Diff Premarket` rows. Diff Revert Premarket rows have been removed and are not seeded. ETH Down reference-average rows additionally use a separate `...down_reference_average_bps...` code/id family so they do not share the old ETH Down previous-result Premarket category or historical metrics. The old ETH `-30s` previous-result Premarket rows remain catalogued for history/settlement but are disabled by schema initialization, while the selected ETH previous-result `-10s` and `-5s` rows remain as legacy timed candidates.

The Dashboard `Paper orders` and `Live orders` tabs include a strategy selector that defaults to `All strategies` and a `Strategy` column on each order row. `Paper orders` loads the first recent-order page from PostgreSQL with the selected strategy filter applied server-side; `All strategies` loads the first global page. `Live orders` loads the same server-side filtered history in pages of 100 rows with `Prev`/`Next` controls, so a strategy with more than 100 live records can be reviewed across its full persisted history. `Paper orders` also joins visible strategy orders to their lifecycle run by `paper_order_id` and shows settlement value, realized PnL, settled time, inferred winning outcome, and `Won` for settled Paper entries. Every row in the `Strategies` `All`, `24 hours`, `6 hours`, and `1 hour` tabs has `Paper orders` and `Live orders` buttons that switch to the matching order tab with that strategy preselected.

`Paper Lost` and `Live Lost` are per-strategy Dashboard fields stored in `strategies.paper_lost_coeff` and `strategies.live_lost_coeff`, both defaulting to `1.00`. `Paper Cnt` and `Live Cnt` are persisted signed loss counters stored in `strategies.paper_lost_counter` and `strategies.live_lost_counter`, defaulting to `0`; both are visible and editable in the Dashboard. Values above `1` enable that mode's loss-counter stake add-on: each Paper/Live loss increments the matching counter, each win decrements it by `1` even below zero, and the add-on is applied only when the matching counter is positive, using `min(Cnt, 2)` times the already computed Paper or Live stake. The final stake is therefore capped at three original stakes: `Stake + Stake * 2`.

Automatic Live-only strategy pausing has been removed. The Dashboard `Paused` checkbox remains a manual full Paper+Live pause, and the Dashboard `Live` checkbox is the persisted live eligibility flag.

The `BTC Up or Down 5m` paper worker watches BTC 5-minute Gamma markets from
`polymarket_gamma_markets`, stores one lifecycle row per enabled market/variant
in `strategy_market_paper_runs`, then creates paper entries for each enabled
variant's configured Paper stake multiplier when it becomes due.

Diff Instant variants use in-memory BTC/ETH/SOL Up/Down counters scoped to the
current UTC day. The counters reset to zero at `00:00 UTC`, then update from
accepted result-ledger rows stored in the existing
`crypto_up_down_5m_websocket_resolved_markets` table.
Accepted sources are `MarketWebSocket`, `TerminalOrderBook`, and
`GammaClosedMarket`. AdjustedDiff Instant variants are parallel copies of Diff,
but their counter state is continuous for the processor session:
it does not reset at `00:00 UTC`. They compute a slow trend zero from an EMA of
raw `Diff = UpCount - DownCount` (`24` points, `12` point warmup, `0.5` max step,
`1` deadband), then compare thresholds against `AdjustedDiff = raw Diff -
trend_zero` for Up-Diff groups and the opposite value for Down-Diff groups.
Diff Progress variants add an in-memory strategy mode on top of the same raw
UTC-day counter. In waiting mode they only count Up/Down/Diff and reset at
`00:00 UTC`. When the side-specific Diff becomes greater than `N`, they switch
to betting mode, buy the opposite outcome with a Paper BUY FAK entry, and use
effective stake `Paper $ * min(Diff - N, 10)`. The `00:00 UTC` counter reset
applies in both waiting and betting mode; if the reset leaves Diff at `N` or
below, the strategy returns to waiting mode. Restart during a
day rebuilds the current-day counter from `00:00 UTC`; the betting/waiting mode is
currently in-memory. AdjustedDiff thresholds are capped at `20`: `1..10`, `15`, and `20`. ShiftDiff
variants keep per-strategy continuous counters, do not reset at `00:00 UTC`, and
apply their configured shift adjustment before threshold evaluation. Every
Revert copies have been removed from the active strategy catalog and seed set.

`CURR Up or Down 5m Diff Up Shift Progress` and `CURR Up or Down 5m Diff Down
Shift Progress` exist for BTC, ETH, and SOL and use separate BTC/ETH/SOL
Dashboard categories. They store `UpCount`, `DownCount`,
`Sum`, and one pending bet in `crypto_up_down_5m_diff_shift_progress_states`.
The Up side evaluates `Diff = UpCount - DownCount`; the Down side evaluates
`Diff = DownCount - UpCount`. `Unit` is the strategy Paper stake amount. After
each resolved 5-minute result, a pending winning bet adds its filled notional to
`Sum`; a losing bet subtracts its filled notional. While `Sum > Unit` and
side-specific `Diff > 1`, the strategy reduces that side's count by one and
subtracts `Unit` from `Sum`. It then buys the opposite outcome with BUY FAK
Paper sizing `Unit * (Diff + 1)` only when `Diff > 0`; `Diff = 0` or negative
Diff skips.

`CURR Up or Down 5m N Diff Shift Progress Premarket` variants are split into
separate BTC/ETH/SOL Dashboard categories. The retained rows are BTC `N=3`,
ETH `N=1,2,3,5`, and SOL `N=1..5`. They evaluate 30 seconds before the 5-minute market opens and use the
persistent raw `Diff = UpCount - DownCount`. Resolved older markets come from
the result ledger; the latest market is synthesized from the current reference
price at the premarket sample point. Positive Diff buys Down, negative Diff buys
Up, and Diff 0 skips because both direction and `abs(Diff)` multiplier are zero.
Each entry uses BUY FAK sizing `Unit * abs(Diff)`. When `abs(Diff)` reaches
`N`, the strategy enters persistent damping mode, resets `Sum`, then moves Diff
one step toward zero each time `Sum > Unit`; reaching Diff 0 exits damping.

`CURR Up or Down 5m N Diff Limit Progress Premarket` variants use separate
ETH/SOL Dashboard categories; the retained ETH and SOL rows use `N=1..5`, while
the five BTC rows were retired. They also evaluate 30 seconds before the 5-minute market opens, keep
persistent UTC-day `UpCount`, `DownCount`, and `Sum`, reload them after service
restart, and reset them at `00:00 UTC`. Older market results come from the
result ledger, while the latest previous market is synthesized from the current
reference price. Raw `Diff = UpCount - DownCount`: positive Diff buys Down,
negative Diff buys Up, and Diff 0 skips. Each entry uses BUY FAK sizing
`Unit * min(abs(Diff), N)`; Diff itself can keep growing beyond `N`, only the
stake multiplier is capped.

`CURR Up or Down 5m N Diff Real Limit Progress Premarket` variants use separate
BTC/ETH/SOL Dashboard categories; they are seeded for BTC, ETH, and SOL with
`N=1..5`. They use the same premarket timing, UTC-day persistence,
direction rule, and BUY FAK sizing as `Diff Limit Progress`, but the counter is
saturated: when `Diff` is already `N`, another Up result does not increment
`UpCount`; when `Diff` is already `-N`, another Down result does not increment
`DownCount`. Opposite results still move Diff back toward the inside of
`[-N, N]`.

A dedicated fast Diff worker observes Diff, Diff Progress, AdjustedDiff, and ShiftDiff strategies, then evaluates market `T` only after the previous 5-minute market
`T-5m` has one of the accepted result rows; if that previous result is missing
long enough, the strategy skips the current market with
`diff_counter_previous_market_resolved_event_missing` and creates no Paper
order. The first `00:00 UTC` raw Diff market starts from zero and does not
require the `23:55 UTC` result from the previous day. Raw Diff snapshots are
still written to `crypto_up_down_5m_diff_snapshots`. Successful Diff-family
entries simulate BUY FAK taker fills from current executable ask depth. Diff
Progress entries reuse the same FAK path, but override the effective Paper stake
with `Paper $ * min(Diff - N, 10)` while the strategy is in betting mode. The
Diff Shift Progress rows use their persistent state table rather than the
in-memory Diff counters, but they share the same immediate FAK ask-depth
execution path and `DiffCounterInstantMaxPrice` cap.
`BtcUpDown5mStrategy:DiffCounterInstantMaxPrice` setting defaults to `1.00`, so
current Diff-family entries are effectively uncapped by the old `0.50` price
limit and no longer place resting BUYs at `0.50` when the market is above
half. The main BTC strategy worker no
longer places Diff-family entries; it continues to settle entered runs through
the shared strategy lifecycle path.

The BTC strategy worker also watches enabled ETH/SOL 5-minute Gamma markets for crypto
Middle, Binance bps, and fixed Up/Down bps
variants, plus BTC/ETH/SOL 15-minute Gamma markets for fixed Up/Down bps
variants, then settles their filled Paper rows through the same strategy
lifecycle tables. With `BtcUpDown5mStrategy:PaperTakerPricingEnabled=true`, the
standard non-`Gamma` variants use Gamma only for market/outcome/token mapping and
settlement metadata. The worker prices both outcome assets from fresh
CLOB/WebSocket order-book depth, falls back to REST CLOB `/book` when cached
depth is missing or stale, computes executable ask-depth VWAP, and stores
quote/order-book context in run diagnostics when a run is skipped. Middle,
Binance, crypto Middle, and crypto Binance bps `Instant`
variants submit BUY FAK-style Paper entries from selected-outcome executable ask
depth, refusing any required BUY price above
`BtcUpDown5mStrategy:InstantOpeningLimitMaxPrice` (`0.65` by default) with skip
reason `instant_price_above_max`. Fixed Up/Down bps `Instant` variants bypass
that cap and can enter at any valid executable ask price below `1.00`. Diff-family Instant variants use the same FAK
path with `DiffCounterInstantMaxPrice=1.00`, so they take available ask depth
instead of falling back to `0.50`. Partial fills keep only actually filled
shares/notional; zero-fill cases are skipped or rejected. Paper opening-limit
entries do not apply the opposite-outcome open-order guard, so strategy tests can
record both sides of one market. Live-shadow preflight still rejects a new Live
BUY when the same condition already has an open Live BUY on a different outcome.
Non-Instant Paper open orders use GTD BUY sizing, synthetic strategy wallets,
fill accounting, and settlement through the shared paper order pipeline; Instant
Paper entries fill immediately from available ask depth and cancel any remainder.
Linked live orders are created by the Paper/Live-shadow path when a strategy's
Dashboard `Live` flag is enabled and all live gates pass.

Before an opening-limit GTD order can expire or a BTC settlement path can skip it as `gtd_limit_not_filled`, the scheduled Paper open-order worker, market-data updater, and BTC settlement processor run the same conservative submit-snapshot fill guard.

Paper fill accounting deliberately uses the submitted paper limit price as the fill price even when visible book depth or an observed trade is better than the limit. The better observed depth/trade price remains in fill evidence only; this makes Paper PnL stricter for strategy comparison.

BTC due-entry placement is selected globally by `entry_due_at_utc` across enabled non-Diff/AdjustedDiff/ShiftDiff variants, capped by `MaxEntriesPerCycle`, and expanded at the final boundary to include every run with the same due timestamp, so one market-open burst is not split across worker cycles. Within the same due timestamp, Live-enabled strategies are processed first; among those, the worker uses an in-memory Live realized PnL snapshot prepared between cycles, so the next market prioritizes the highest Live realized strategies without querying performance during entry decisions. The service config currently uses `EntryGraceSeconds=60`, `MaxEntriesPerCycle=3000`, and `MaxConcurrentEntryDecisions=32`. A temporary `crypto_reference_fetch_failed` decision remains observed only inside that existing entry-grace window and is evaluated with a new current-price lookup on the next due-entry pass; the failed batch lookup is not reused across passes, and no stale reference price is accepted. Once the grace window expires, that reason is no longer deferred and the strategy follows its existing terminal expiry or decision-skip path. This total limit is shared across main, fast Diff, previous-result, and pre-open sell-exit flows. `FastDiffReservedEntryDecisionSlots=8` limits non-priority flows to 24 concurrent decisions while preserving eight of the same 32 total slots for fast Diff; it does not increase total concurrency. PreviousResult market/signal work is warmed before any decision slot is acquired, so ledger, bounded Gamma fallback, and close-book calculations do not occupy the shared decision capacity.

All strategy observation flows share one BTC/ETH/SOL Gamma snapshot for `ObservationMarketSnapshotTtlMilliseconds=5000`. A refresh uses one query bounded to the observation window instead of independently scanning the full active Gamma table for main, fast Diff, PreviousResult, and maker high-water observation. Partial indexes on active `end_date_utc` and `event_start_time_utc` support both this query and recently-ended result polling. New observed strategy/market rows are sent to PostgreSQL in JSONB batches with one `ON CONFLICT DO NOTHING` statement per chunk. A bounded in-memory key cache suppresses repeat insert attempts for the same active market; a failed batch removes its reservations so the next cycle retries instead of losing the observation.

Fixed Up/Down bps Instant variants run in a separate previous-result worker; it processes a five-minute run only after the immediately previous outcome exists in the resolved-market ledger. The signal then reads the ledger through the first known outcome change and uses the bounded Gamma/close-book path only for missing older streak markets. The result-polling worker self-resolves BTC/ETH/SOL 5-minute outcomes from archived Binance start/end reference prices before Gamma polling: BTC reads `btc_up_down_5m_odds_ticks`, ETH/SOL read `crypto_up_down_5m_odds_ticks`, and a non-equal end price writes a `ReferenceStartEnd` row to `crypto_up_down_5m_websocket_resolved_markets`. The self-resolve path requires at least `ReferencePriceResultMinSamples` samples and an end tick no older than `ReferencePriceResultMaxEndAgeMilliseconds`; exact price ties still wait for another result source. The generic, Diff-family, and previous-result due-entry workers share the fixed-rate cadence controlled by `BtcUpDown5mStrategy:DiffCounterFastPollIntervalMilliseconds` (`500` ms in service config): the first cycle is immediate, later ticks are anchored by `PeriodicTimer`, a long cycle is never overlapped, and missed ticks are coalesced so completion does not add another full interval. Diff/AdjustedDiff/ShiftDiff variants remain isolated to their dedicated due-entry worker, so a fresh previous-market result can update counters and trigger Paper orders without waiting for the heavier observe/maker/settlement cycle. After deployment, verify the scheduling change with `scripts/check-strategy-entry-latency.ps1 -HostOverride 192.168.0.101 -ExpectedCommit <commit> -MaxDelaySeconds 3 -LookbackMinutes 30 -RequireSplitCycleKinds`; completion requires a fresh full-boundary window with zero enabled Up/Down 5m entries above `3s`. Missing PreOpen rows remain disabled by runtime settings and the database guard skips deleted PreOpen run rows, so current-market opening/delayed strategies are not delayed by removed PreOpen strategies. Middle reference opening-limit variants get a shared fast skip pass before generic per-run placement: cached BTC reference prices and market lookups are reused, rejected runs are batch-updated in one repository call, and only enterable runs continue to the ordinary order path. Accepted BTC Paper signals and orders are inserted through one transactional repository call to avoid a separate connection/round-trip for the order row. Opening-limit stake sizing also caches CLOB `/book` fallback tasks per asset inside a cycle, so a burst of variants sharing the same token does not repeat the same missing/stale book request hundreds of times. BTC due settlement now uses the same global-queue shape across all variants instead of walking one variant at a time: ended `Entered` runs are selected by market end, filled/partially-filled orders are prioritized, the batch is capped by `MaxSettlementsPerCycle`, and work runs concurrently up to `MaxConcurrentSettlements`. Per-cycle Gamma market metadata is cached for settlement so a burst of strategies sharing the same token does not repeat the same closed-market lookup or timeout.

Child/Parent selection no longer runs inside the main or fast due-entry loops. `BtcUpDown5mChildParentRefreshWorker` runs once per five-minute interval, by default 60 seconds after market start (`ChildParentRefreshDelaySeconds=60`), to keep this work outside the opening-time due-entry path. Its lookback query reads the latest 24-hour run set once, aggregates each run into its first eligible hour bucket, and calculates the `1..24h` cumulative totals without joining every settled run to every qualifying window. Existing assignment selection, Futures exclusion, and assignment-history persistence are unchanged.

BTC strategy cycle timings are persisted to `btc_up_down_5m_strategy_stage_timings` for failed stages, stages lasting at least 1 second, and stages that process runs/orders/markets. Rows share a `cycle_id` and include `cycle_kind` (`main`, `main_due`, `fast_diff_due`, `fast_diff_observe`, `previous_result_due`, `previous_result_observe`, or `child_parent_refresh`), `flow_name`, `stage_name`, timestamps, duration, run/order counts, observed-market counts, and earliest/latest due timestamps. Each due-entry decision batch also writes a `*.wait_breakdown` row. Its `detail` separates `decision_semaphore_wait`, `market_lookup`, `reference_decision`, `order_book`, and `placement_lock_wait`, with `count`, cumulative `total_ms`, and per-call `max_ms`; `duration_ms` is the largest observed wait. This table is intended for production latency diagnosis: it shows whether a one-minute gap came from due-run SQL, previous-result filtering, bulk middle-reference skips, market warmup, decision tasks, deferred persistence flush, observe, maker high-water, sell exits, settlement, or one of the measured internal waits. A useful recent timeline query is:

```sql
SELECT
    started_at_utc AT TIME ZONE 'Europe/Sofia' AS started_bg,
    cycle_kind,
    flow_name,
    stage_name,
    detail,
    duration_ms,
    run_count,
    entries_placed,
    runs_skipped,
    runs_settled,
    markets_observed,
    earliest_entry_due_at_utc,
    latest_entry_due_at_utc,
    succeeded,
    error_message
FROM btc_up_down_5m_strategy_stage_timings
WHERE started_at_utc >= now() - interval '1 hour'
ORDER BY started_at_utc DESC;
```

Use the production latency gate after deploying a service build. It reads `POLYCOPYTRADER_POSTGRES_CONNECTION`, can override only the host for remote checks, and exits non-zero if any enabled Up/Down 5m strategy entry in the checked window exceeds the delay budget:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-strategy-entry-latency.ps1 -HostOverride 192.168.0.101 -ExpectedCommit 02cf2d6 -MaxDelaySeconds 3 -LookbackMinutes 30
```

Diff Countertrend counters use raw UTC-day counts reset at `00:00 UTC`. After each accepted BTC/ETH/SOL 5-minute result in the current UTC day, the processor updates `UpCount` or `DownCount`, computes `Diff = UpCount - DownCount`, and stores `DiffCount` as a diagnostic cumulative sum of observed Diff values. Strategy thresholds compare against raw `Diff`; `DiffCount` no longer shifts either side of the counter. AdjustedDiff Countertrend keeps a separate continuous in-memory counter, does not reset it at `00:00 UTC`, and compares thresholds against raw `Diff` adjusted by its slow EMA trend zero. ShiftDiff keeps per-strategy continuous counters and applies its configured shift before comparison. BTC/ETH/SOL Up/Down Diff Premarket rows run 30 seconds before open for thresholds `1..10` and buy the opposite side when `UpCount - DownCount` or `DownCount - UpCount` reaches the threshold. Revert variants have been removed. When the immediately previous result is still missing, a Diff-family run stays pending until four minutes after its own market start; only then it is skipped with `diff_counter_previous_market_resolved_event_missing`.

Late-entry BTC GTD orders whose due time is after the market midpoint bypass the market-end safety offset and use the fallback TTL/market-end cap instead; this remains relevant to any remaining late-entry variants.

PreOpen fixed-direction strategy rows were physically removed from production and are no longer seeded. Missing PreOpen strategy rows are treated as deleted/disabled by the worker, and the strategy-market-run insert guard skips deleted PreOpen run rows so historical worker code cannot recreate their run history.

BTC close-book result inference now starts capturing CLOB `/book` snapshots for active BTC strategy markets and ETH/SOL 5-minute or 15-minute markets during the final `BtcUpDown5mStrategy:CloseBookCaptureLookbackSeconds` seconds before close, throttled by `CloseBookCaptureIntervalSeconds`; service config uses 60 seconds and 10 seconds. Previous-result bps logic first tries the current close-book fetch, then falls back to the latest stored `order_book_snapshots` row for that token if the book stops responding after close. The result no longer requires a full midpoint: `Up` midpoint still wins at `>= 0.5`, but a single `Up best_bid >= 0.5` also infers `Up`, a single `Up best_ask < 0.5` infers `Down`, `Down best_ask <= 0.5` infers `Up`, and `Down best_bid > 0.5` infers `Down`. If the available one-sided signals conflict, the run is skipped with `btc_close_book_inference_conflict`; if no usable book or stored snapshot exists, it is skipped with close-book diagnostics.

A dedicated BTC order-book refresh worker keeps the shared market-data cache warm for active and near BTC Up/Down strategy markets by polling CLOB `/book` every `BtcUpDown5mStrategy:OrderBookRefreshIntervalMilliseconds` milliseconds, default `1000`. It covers the BTC `5m`, `15m`, `1h`, and `4h` series used by enabled strategy variants, registers the same asset ids with the active WebSocket subscription registry, and stamps REST snapshots with the local receive time before applying them to the cache, so the strategy freshness check measures local cache age rather than an already-old exchange timestamp. This reduces `missing_orderbook_cache_stale` skips while still leaving stale-cache rejections visible when neither WebSocket nor REST refresh can keep up.

When non-Instant `Middle` or `Middle Revert` rows do not yet have enough own settled rows for dynamic break-even pricing, they bootstrap the GTD BUY limit from the selected outcome order book rather than blocking the first orders. The bootstrap uses `best_ask` when it is at or below `0.50`; otherwise it uses `best_bid + tick`, capped at `0.50`. If neither usable book price exists, the run is skipped with book-bootstrap diagnostics.

`BTC Up or Down 5m Middle N` rows exist for `N=100,90,80,...,10`, with matching `1..100 bps`, `Instant`, `Revert`, and `Revert Instant` variants. `N` is the number of latest sampled Binance BTC/USDT reference prices used to compute the arithmetic mean; a strategy skips with `btc_reference_samples_insufficient` until its cache has at least `N` samples. Standard Middle compares the current latest Binance trade-stream price with that N-sample mean and buys `Down` above the mean or `Up` below it; Revert inverts that direction. Bps variants require the current price's absolute deviation from the N-sample mean to reach the configured threshold. Matching `Instant` variants keep the same signal and bps threshold, then submit a BUY FAK taker entry from the selected outcome's executable ask depth, with the same instant size calculation and `InstantOpeningLimitMaxPrice` cap used by Binance instant variants.

`ETH Up or Down 5m Middle` and `SOL Up or Down 5m Middle` mirror the same N-grid against the matching Binance `<asset>USDT` trade stream. Each asset has base Middle N, Middle N `1..100 bps`, Middle N `1..100 bps Instant`, Middle N Revert, Middle N Revert `1..100 bps`, and Middle N Revert `1..100 bps Instant` rows for `N=100,90,80,...,10`. They compare the latest ETH/SOL stream price to that asset's N-sample cached mean from `BinanceCryptoReference`; crypto skip reasons use the `crypto_reference_*` prefix, and raw decision JSON stores `reference_asset_symbol`, `reference_binance_symbol`, required/reference sample counts, and crypto move-from-mean fields. These opening-limit rows can enter the Paper/Live-shadow path when their Dashboard `Live` flag is enabled and all live gates pass.

`BTC Up or Down 5m Up 1..50 bps Instant` and `BTC Up or Down 5m Down 1..50 bps Instant` use the previous-result streak and cumulative BTC start-to-close bps gate, but keep only one fixed countertrend side. The former BTC 15-minute fixed Up/Down bps Instant rows were removed from production and are no longer seeded because observed 15-minute volume/liquidity was too thin for current live use. The `Up` variants enter only when the previous streak points to buying `Up` after a `Down` streak; if the countertrend side is `Down`, they skip with `btc_previous_market_move_fixed_outcome_mismatch`. The `Down` variants mirror that behavior and enter only when the countertrend side is `Down`. Accepted entries use the executable ask-depth FAK path with an effective max BUY price of `1.00`, so the old `InstantOpeningLimitMaxPrice` default of `0.65` no longer blocks these fixed Up/Down bps entries.

`ETH/SOL Up 1..50 bps Instant` and `ETH/SOL Down 1..50 bps Instant` reuse the same crypto streak/move gate, but keep only the requested fixed countertrend side before using the same uncapped fixed-outcome FAK path with effective max BUY price `1.00`. The former ETH/SOL 15-minute fixed Up/Down bps Instant rows were removed from production and are no longer seeded. Skip strategy rows have been removed from the seed set.

`BTC Up or Down 5m Binance` waits until the BTC 5-minute market accepts orders, compares the latest Binance BTC/USDT trade price with the first archived BTC reference for that market from `btc_up_down_5m_odds_ticks`, buys `Up` when current BTC is above start, buys `Down` when current BTC is below start, skips equality, and creates a GTD BUY capped at `0.50` with the same BTC opening-limit expiration policy. `BTC Up or Down 5m Binance` bps variants from `1 bps` through `50 bps` in `1 bps` increments keep that baseline price and direction logic but skip unless the absolute BTC move from market start is at least the configured bps threshold; the skip reason is `btc_reference_move_below_bps_threshold`. The matching `Instant` bps variants keep the same signal and threshold, but submit a BUY FAK taker entry from current selected-outcome ask depth, taking whatever liquidity is immediately available up to the computed size and cap. `BTC Up or Down 5m Binance 15s`, `30s`, and `45s` use the same direction/price rule but wait for the configured delay after market open before reading the current Binance reference. `BTC Up or Down 5m Binance 45`, `47`, and `49` use the same direction signal but submit fixed GTD BUY limits at `0.45`, `0.47`, and `0.49` respectively, so their fill rate and payoff can be compared against the `0.50` baseline. If the archive has not produced a start reference yet, the observed run waits for the next cycle instead of being permanently skipped.

ETH/SOL Binance bps rows were removed from the seed set and local/server history. `ETH Up or Down 5m Down 9 bps` keeps the same previous-result fixed Down signal as the matching Instant strategy. The legacy selected ETH Premarket variants remain only at `-10s` for `40..42 bps` and `-5s` for `30..38 bps`; each samples the previous ETH reference price the same number of seconds before previous market close and enters the next market the same number of seconds before open.

`BTC/ETH/SOL Up or Down 5m Up/Down N bps Reference Average Premarket` reference-average variants run 30 seconds before the 5-minute market open. `N` covers `1..10` in steps of `1`, then `15..100` in steps of `5`. For every configured `24h`, `12h`, `6h`, `3h`, `90m`, `45m`, `20m`, and `10m` crypto reference window, its in-memory `middle` average participates whenever the window has at least one valid sample and a positive calculated average. `IsFullWindow`, missing internal buckets, and incomplete coverage do not exclude an otherwise usable window and do not by themselves stop the decision. The strategy computes a maximum boundary `Amax` and a minimum boundary `Amin`, preferring the longer window when equal prices tie. An `Up` trigger compares the current Binance price with `Amax` and buys `Down` at `>= N` bps above it; a `Down` trigger compares with `Amin` and buys `Up` at `<= -N` bps below it. Neutral rows test both sides of the envelope: above `Amax` by at least `N` buys `Down`, below `Amin` by at least `N` buys `Up`, and every price inside the envelope or less than `N` bps outside it skips. Every price-bps calculation uses the same denominator: the oldest available real bucket in the configured `24h` window, which may itself be incomplete. The nested cache inserts every valid tick into every configured window, so usable shorter-window data also supplies usable `24h` data and a first bucket; no alternate denominator is substituted. If that `24h` start price is nevertheless absent, the decision fails closed with `reference_average_bps_denominator_24h_available_start_price_missing` as an internal configuration/integrity violation. Diagnostics use `decision_source=reference_price_average_envelope_bps_premarket_v5`, `algorithm=5`, and `contract=max_min_available_data_envelope_available_24h_start_denominator`; they record `boundary_requires_full_window=false`, `boundary_uses_available_data=true`, `incomplete_data_blocks_decision=false`, both boundaries, both moves, the selected boundary and its completeness, and the `24h` denominator evidence. ETH Down reference-average rows also use distinct strategy codes from legacy ETH Down previous-result Premarket rows; the legacy `... Down N bps Premarket` rows stay in the catalog only for historical runs and settlement. These rows simulate the same taker BUY from executable ask depth using worst price `1 - tick`: fills are recorded at ask-depth VWAP, partial fills keep only the filled notional, and zero-fill cases are skipped/rejected instead of being treated as a buy at the cap. Their Live-shadow entry sends a BUY `FAK` market amount with the same worst-price cap; any unfilled remainder is cancelled by the exchange and a zero-fill response is stored as a rejected live entry. These rows are seeded with runtime `Live` disabled by default, except a first migration can copy an existing ETH Down legacy runtime flag to the matching new reference-average row before disabling the legacy row; enabled rows can enter the Paper/Live-shadow path when their Dashboard `Live` flags are enabled and all live gates pass.

`BTC/ETH/SOL Up or Down 5m Optimized Average Premarket` contains 144 base Paper experiments: 84 ETH rows over the full 28-threshold Up/Down/neutral grid and 30 rows each for BTC and SOL over the `1..10` Up/Down/neutral grid. These strategies do not force the 3-hour average or remove any window. They first run the ordinary Reference Average available-data envelope decision: every usable configured window participates regardless of completeness or gaps, with the same Max/Min selection, longer-window tie-break, trigger direction, and inclusive `N bps` threshold. Only then do they keep the entry when the direction-relevant selected boundary uses `3h`. Thus an Up trigger requires the usable `3h` average to be `Amax`, while a Down trigger requires it to be `Amin`; a neutral trigger follows whichever envelope side crossed. An incomplete `3h` can be selected and matched. Diagnostics record the selected boundary, its completeness, selected and required windows, and whether they matched. These rows cannot create Live-shadow or Live orders and cannot become parents for Child strategies.

`BTC/ETH/SOL Up or Down 5m N bps LowEnter Average Premarket` adds 84 neutral Reference Average Paper experiments: 28 thresholds per asset (`1..10`, then `15..100` step `5`). Each row keeps the ordinary available-data Max/Min envelope signal and contrarian outcome selection, including participation by every usable complete or incomplete configured window, then submits the simulated BUY FAK with a hard maximum order price of `0.50`. Paper fills only ask levels at or below `0.50`, permits a partial fill, and cancels the unfilled remainder; VWAP is recorded only as an execution outcome and never authorizes or rejects that same order. New rows are enabled for Paper with `Live=false`, are enforced as Paper-only even if `Live` is toggled manually, and appear in separate per-asset `Bps LowEnter Average Premarket` Dashboard categories. They start collecting new history after deployment and do not rewrite the history of their source strategies.

`ETH Up or Down 5m N bps Reference Average Maker GTD Premarket` adds 28 independent Paper-only Maker experiments for neutral ETH thresholds `1..10`, then `15..100` step `5`; the original FAK rows are unchanged. Each clone inherits the source Reference Average available-data signal, so every usable configured window can participate whether complete or incomplete and gaps alone do not block the signal. This is the only behavior changed for the Maker family: selected outcome, timing, stake sizing, risk gates, and execution remain unchanged. New `maker_gtd_paper_v2` placements make at most ten fresh attempts and compute the maximum-resting BUY candidate `floor_to_tick(min(bestAsk - tick, 0.99))` (the highest tick-aligned price strictly below `bestAsk` when the venue book is tick-aligned), freeze one share-based `GTD` intent with `postOnly=true`, and obtain a second fresh book observation solely to emulate the venue's PostOnly acceptance check. A BUY limit at or above that second best ask is rejected and the next attempt starts with a new intent; a lower limit becomes one `Resting` order. The wire GTD expiration is market end, giving the Paper order an effective expiry one minute earlier. While resting, the explicit optimistic `TouchNoDepth` model marks the whole order filled at its own limit when an authoritative later exact-token `last_trade_price` or current best ask is at or below the limit. Equality counts, while queue position, depth, event size, and aggressor side are intentionally ignored. A continuously observed order with no trigger expires unfilled; stale, missing, restarted, or reconnected evidence is classified unavailable. By explicit user approval on 2026-08-09, only this exact 28-strategy family—asset `ETH`, those neutral thresholds, behavior `ReferenceAverageBpsThresholdMakerGtdPremarket`, catalog ID `b7c50005-0000-4000-8223-{100+threshold, zero-padded to 12 digits}`, execution source `eth_reference_average_maker_gtd_paper`, cap `0.99`, and `PaperOnly=true`—is a closed ordinary-Paper exception: its orders, positions, fills, PnL, win rate, and performance intentionally enter standard Paper metrics. Exact-family `maker_gtd_paper_v1` records already persisted with the earlier one-tick-improvement formula are grandfathered for lifecycle completion and historical accounting; persisted contract version/formula fields distinguish them from v2. New v2 placement fails closed unless the exact family predicates pass. Every result must be labeled `optimistic TouchNoDepth Paper; not Live-equivalent; may overstate fills`. Live submission remains disabled, and no alias, clone, descendant, future strategy, different execution source, predicate mismatch, or changed execution semantic inherits the exception.

Maker acceptance evidence applies a one-sided PostgreSQL precision allowance only to the existing lower lifetime bound. The JSON `accepted_at_utc` may be at most five .NET ticks (half a microsecond) earlier than the persisted `paper_orders.created_at_utc`: a five-tick difference is valid, while six ticks still fail closed as `maker_gtd_evidence_unavailable` with detail `order_lifetime_mismatch`. Equality and an accepted timestamp later than creation but before effective expiry remain valid. Effective-expiry equality, the upper lifetime bound, root/nested accepted-timestamp identity, exact-family membership, asset subscription, stale/reconnect/market-data continuity, TouchNoDepth, pricing, GTD/PostOnly, PaperOnly, and Live-disabled gates are unchanged. This correction applies to future and still-active lifecycle work after deployment; it does not reopen, replay, or rewrite any Maker-GTD record that was already terminal before activation.

The available-data migration retains existing strategy IDs, codes, names, thresholds, directions, stakes, execution modes, Paper/Live flags, and runtime controls. Its current static catalog impact is 932 strategies: 764 direct shared-path Reference/Optimized/LowEnter/3h/Maker/LowerEnter variants plus 168 BpsConfirmed/DiffConfirmed composites, split BTC 312, ETH 406, and SOL 214. The shared Filtered dispatch uses the same policy, although the current catalog contains zero Filtered rows. A further 247 Child/Child Progress/Child ROI/Child Progress ROI rows are conditionally downstream-affected because their runtime-selected parent can be one of the non-Paper-only affected Reference or Confirmed variants; this tier contains BTC 96, ETH 63, and SOL 88 rows. A Child decision can actually change only while that row is active, its PnL/ROI gates select an affected parent, and the corresponding assignment remains active. The inclusive potential catalog scope is therefore 1,179 strategies: BTC 408, ETH 469, and SOL 302. The 56 ETH single-window 3Hour variants remain strictly `3h`-only, but their usable `3h` average now participates whether complete or incomplete; other windows cannot become their boundary. Absolute Premarket and Diff Reference Average use independent calculation paths and are unchanged. Because IDs are retained, aggregate historical statistics span the prior and v5 algorithms; raw decisions distinguish the new regime with `algorithm=5` and the versioned decision source.

`BTC/ETH/SOL Up or Down 5m N bps Futures Basis Premarket` variants run 30 seconds before the 5-minute market open. `N` covers `1`, `2`, `3`, `5`, `8`, `10`, `15`, and `20`. They select the three live OKX linear USD fixed-expiry contracts with the nearest distinct expiries at or after the target market end and compare every best-bid/ask mid with the simultaneous OKX `<asset>-USD` index. The `N bps` threshold applies only to the nearest expiry. The second and third expiries confirm only its strict sign: both must be positive when the nearest basis is positive, or both negative when it is negative. Zero or an opposite sign skips with `futures_basis_confirmation_sign_mismatch`; a nearest absolute basis below `N` still skips with `futures_basis_move_below_bps_threshold`. Matching Revert variants perform the same three-expiry confirmation and only then invert the trade direction. All three contracts and their quotes must be present and fresh; there is no two-contract or perpetual fallback. Entries use the same executable ask-depth FAK Paper/Live-shadow path as the reference-average Premarket rows, and the seeded runtime `Live` flag is disabled by default.

`BTC/ETH/SOL Up or Down 5m N Child` modes mirror a selected same-asset non-Child, non-Futures, non-Paper-only parent strategy. Plain Child and Child ROI retain every `N=1..24`. After the staged negative-Gross catalog retirement, Child Progress retains only SOL `N=15,17,19,20,21,22,23,24`; Child Progress ROI retains BTC `N=4,5,6,7,8,9,22`, ETH `N=1,20`, and SOL `N=9`. Plain Child excludes parent strategies whose names contain `Progress`; Child Progress can select them. The parent-selection, minimum-sample, ROI, and Paper-only execution rules are unchanged for every surviving row. Migration `20260713_remove_hopeless_progress_strategies` removed the earlier exact 57 rows. The additional exact 217 variants are now retired only from catalog/seed logic; their existing stopped rows and history are deliberately preserved until the later separately authorized cleanup.

`BTC/ETH/SOL Up or Down 5m N Diff Reference Average Premarket` variants are one-row-per-asset strategies that choose the direction themselves 30 seconds before market open. `N` covers `1..10`, then `15`, `20`, `25`, and `30`. The strategy rebuilds a sliding 24-hour `Diff = UpCount - DownCount` from `crypto_up_down_5m_websocket_resolved_markets`, without resetting at the UTC day boundary, and appends the synthetic previous 5-minute result inferred from the current Binance reference price. It then averages that running Diff over full `24h`, `12h`, `6h`, `3h`, `90m`, and `45m` windows only; `20m` and `10m` are intentionally excluded for this family. The selected reference is the average whose absolute value is farthest from zero. If `currentDiff - selectedAverageDiff >= N`, the strategy buys `Down`; if `currentDiff - selectedAverageDiff <= -N`, it buys `Up`; otherwise it skips. Entries use the same executable ask-depth FAK Paper/Live-shadow path as the reference-average Premarket rows.

Confirmed Average Premarket variants evaluate two complete signals for the same market and enter only when both produce the same non-null `Up` or `Down` outcome. Bps Confirmed rows execute their linked neutral Bps decision as the base and use the asset-specific Diff threshold only as confirmation; Diff Confirmed rows reverse those roles. The entered outcome, stake override, and price override come from the base decision. Missing base signals keep the base rejection reason; missing confirmation signals use `confirmed_average_confirmation_signal_not_available`; opposite outcomes use `confirmed_average_signal_mismatch`. Structured decision JSON stores both linked strategy IDs/codes, both nested decisions, both outcomes, and the agreement result. Diff history and the synthetic previous-market signal are shared within one processing cycle so multiple confirmed rows do not repeat the same 24-hour history read.

`BTC Up or Down 5m Binance Clever` uses the same Binance start-relative direction, but prices the entry from the odds archive instead of always using `0.50`. It estimates target outcome fair value from recent `btc_up_down_5m_odds_ticks` samples with similar direction-normalized BTC move from market start, similar seconds-to-close, and comparable book quality. The baseline Paper BUY limit is `fair value - 0.03`, discounted for one-sided/wide/non-WebSocket book evidence, capped at `OpeningLimitMaxPrice` / `0.50`, and floored to the configured tick. `BTC Up or Down 5m Binance Clever Aggressive` uses a `0.01` fair-value margin, while `BTC Up or Down 5m Binance Clever Conservative` uses `0.05`; `BTC Up or Down 5m Binance Edge 2/4/6` run the same fair-value model with `0.02`, `0.04`, and `0.06` required edge. It skips when the current market has no archived odds snapshot, the current spread is too wide, the historical sample is under 20 ticks, or the computed safe price is non-positive. Binance bps `Instant` variants for BTC and crypto markets use immediate FAK ask-depth execution, but refuse any required BUY price above `BtcUpDown5mStrategy:InstantOpeningLimitMaxPrice` (`0.65` by default) with skip reason `instant_price_above_max`.

`BTC Up or Down 5m Prev Score Countertrend 10..50` reads only the immediately previous BTC 5-minute market from `btc_up_down_5m_odds_ticks`; it does not score the current market for a current-market entry. For the previous market it computes BTC deviations from the archived Binance start price, winsorizes the deviation tails, and takes a timestamp-duration-weighted average. A positive score means the previous market was biased `Up`, so the next market buys `Down`; a negative score buys `Up`; a score inside `PreviousScoreCounterTrendEpsilonScore` or with fewer than `PreviousScoreCounterTrendMinSamples` skips. The decision JSON also stores this score as signed bps (`previous_score_bps = previous_score * 10000`), absolute bps, and selected signal bps; the Dashboard `Strategies` tab aggregates average signed score bps, average signal bps, and latest signal bps for these rows. The 9 numbered BTC variants share the same previous-market signal but use their own fixed GTD BUY limit prices from `0.10` through `0.50` in `0.05` steps. The singular `BTC/ETH/SOL Up or Down 5m Prev Score Countertrend` variants use that same countertrend signal but take executable ask depth immediately with any unfilled remainder cancelled instead of placing a fixed-price GTD order; ETH and SOL read the immediately previous market from `crypto_up_down_5m_odds_ticks` for their own asset. `BTC/ETH/SOL Up or Down 5m Prev Score Countertrend Premarket` variants enter 30 seconds before the target 5-minute market opens and score a synthetic 5.5-minute reference window ending at that entry time: the last minute of the market before the currently running market plus the first 4 minutes 30 seconds of the currently running market. The first valid sample in that synthetic window becomes the score start price; positive score buys `Down`, negative score buys `Up`, and neutral or insufficient samples skip. `BTC/ETH/SOL Up or Down 5m Prev Score Countertrend Revert` variants use the same previous-market score but keep the previous bias direction: previous `Up` buys `Up`, previous `Down` buys `Down`, and use the same immediate executable ask-depth entry model. `BTC/ETH/SOL Up or Down 5m Prev Score Countertrend Premarket Revert` variants combine the 30-second premarket timing and synthetic 5.5-minute score window with Revert direction: synthetic `Up` buys `Up`, synthetic `Down` buys `Down`.

The `BinanceBtcUsdReference` service keeps a live WebSocket connection to `wss://data-stream.binance.vision:443/ws/btcusdt@trade`. The latest trade price is used for immediate BTC Middle decisions, while the rolling cache samples that latest trade once per `BinanceBtcUsdReference:SampleIntervalSeconds`, default `60`, and keeps the latest `BinanceBtcUsdReference:WindowSize` samples, default `100`, in memory. The cache snapshot includes source, latest sampled price, source update time from the trade event, sample count, full-window flag, and arithmetic mean over the retained samples. It is exposed locally through `GET /btc-usd-reference` on the IPC listener. This is a research/reference feed, not the CLOB order-book price used for BTC Paper GTD limits.

`BtcUpDown5mOddsArchive` stores a compact research archive in `btc_up_down_5m_odds_ticks` while BTC 5-minute markets are active. Each tick records the current Binance BTC/USDT reference price, the first archived BTC price for that market, BTC move from that reference, Up/Down best bid/ask/mid or one-sided proxy, quote source (`websocket_cache` or `clob_rest`), quote age, and diagnostics for missing books. It is intended for testing whether BTC movement from market start explains or predicts Polymarket odds, without enabling high-volume global order-book persistence.

`CryptoReferencePriceHistory` stores one market-independent reference-price tick per configured asset every `10` seconds in `crypto_reference_price_ticks`. The default assets are `BTC`, `ETH`, and `SOL`. On service start, the worker loads the last `24` hours from that table into an in-memory rolling cache, then keeps it current as new ticks are written. The cache maintains averages for `24h`, `12h`, `6h`, `3h`, `90m`, `45m`, `20m`, and `10m`. Each window uses a proportional sample step targeting `60` samples: `10m` uses `10s`, `20m` uses `20s`, `45m` uses `45s`, `90m` uses `90s`, `3h` uses `180s`, `6h` uses `360s`, `12h` uses `720s`, and `24h` uses `1440s`. This table is the clean BTC/ETH/SOL exchange-rate source for future strategy moving-average signals; the older odds tables remain market-tied diagnostics.

`OkxExpiryFuturesReference` reads only public OKX data for BTC, ETH, and SOL. Every cycle it refreshes fixed-expiry tickers plus each asset's `<asset>-USD` index, while the live instrument catalog is refreshed separately. For each Futures Basis Premarket decision it selects the three live linear USD fixed-expiry contracts with the earliest distinct expiries at or after the target 5-minute market end, with no maximum horizon and no perpetual fallback. The nearest contract supplies the signed `N bps` threshold signal; the next two must both confirm that raw basis sign before the standard or Revert direction rule is applied. Raw decision JSON retains the legacy singular fields for the nearest contract and also records all three instruments, expiry ranks and roles, prices, quote timestamps, bases, signs, horizons, and confirmation result. Existing strategy IDs, codes, runtime flags, and stored history remain unchanged. New rows use `okx_three_expiry_confirmed_futures_basis_bps_premarket` or its Revert counterpart as `decision_source`, so they can be separated from both the former Binance history and the one-expiry OKX history.

`BtcUpDown5mArbitrageScanner` is a read-only covered-arbitrage scanner for active BTC 5-minute binary markets. Every cycle it reads fresh Up and Down CLOB ask depth from the shared WebSocket cache or REST `/book`, computes whether equal shares of both outcomes can be acquired for less than the guaranteed payout after `SafetyBufferPerShare` and `MinNetProfitUsd`, and stores diagnostics in `btc_up_down_5m_arbitrage_scans`. Rows with `would_arbitrage=true` mean the order book showed a covered opportunity at scan time; the worker still does not create Paper, dry-run, or Live orders.

`CryptoUpDown5mOddsArchive` extends the same archive pattern to non-BTC crypto 5-minute and 15-minute markets, currently `ETH` and `SOL`. It stores rows in `crypto_up_down_5m_odds_ticks` with the asset symbol, Binance `<asset>USDT` trade-stream price, the first archived market-start reference, asset move from start, Up/Down book proxy, source/age, and diagnostics. The companion `BinanceCryptoReference` service uses one Binance combined WebSocket stream for those symbols, exposes the latest price for start-relative crypto strategies, and samples each asset once per `BinanceCryptoReference:SampleIntervalSeconds`, default `60`, into a `BinanceCryptoReference:WindowSize`, default `100`, in-memory rolling mean for ETH/SOL Middle. This data feeds ETH/SOL fixed and reference-average strategies; Futures Basis uses the separate OKX fixed-expiry/index feed. Live placement for opening-limit entries is controlled by each strategy's runtime Dashboard `Live` flag plus the normal live gates.

`CryptoUpDown5mResultPolling` measures how quickly BTC/ETH/SOL 5-minute results
become available and also feeds the Diff result ledger. It polls each recently
ended concrete market slug every 5 seconds by default, stores status, attempt
count, first-closed time, first-winner time, winning outcome, and delay seconds
in `crypto_up_down_5m_result_polling_observations`, and writes usable market
results into `crypto_up_down_5m_websocket_resolved_markets`. Provisional
terminal order-book rows use source `TerminalOrderBook`; later Gamma closed
metadata uses source `GammaClosedMarket`.

Due-run Paper settlement keeps Gamma primary and unchanged. The processor first uses the existing Gamma success predicate—the first metadata row with `Resolved=true` and a nonblank `WinningOutcome`. A valid Gamma winner causes zero canonical-ledger lookups, and ledger data cannot reject or override it. Only when Gamma supplies no resolved winner may a BTC/ETH/SOL five-minute run consult `crypto_up_down_5m_websocket_resolved_markets`. The fallback requires exactly one row for normalized reference asset plus `market_start_utc`, current exact membership in `StrategyIds.UpDown5mStrategyVariants`, and exact market id, condition id, slug, five-minute start/end, `Up`/`Down` winning outcome, nonempty winning asset, event time at or after market end, and winning/selected token mappings from the paired `polymarket_gamma_markets.outcomes_json` and `clob_token_ids_json` arrays. The only accepted ledger sources are `GammaClosedMarket`, `MarketWebSocket`, and `BinanceTimedClose`. `ReferenceStartEnd`, `TerminalOrderBook`, unknown sources, missing winner evidence, early events, non-five-minute or non-current variants, duplicate rows, and every identity or token mismatch remain fail-closed and leave the run `Entered`.

`BinanceTimedClose` is a derived Binance close result, not a direct Polymarket resolution event; its explicitly approved authority is limited to this exact Paper fallback after Gamma has no winner and every source, catalog, asset, identity, token, and event-time gate passes. It never authorizes or changes Live trading and may differ from a later Polymarket result.

Every fallback-settled run durably stores a versioned `settlement_resolution` object in `skip_diagnostics_json`. SQL `NULL` becomes a new object; an existing JSON object retains every member before the evidence member is added. The evidence records `contract_version=btc_up_down_5m_resolved_ledger_settlement_v1`, ledger id/source, normalized asset, exact market/condition/slug/start/end, winning outcome/asset, event timestamp, and `validation_result=exact_identity_token_time_match`. Semantically identical evidence is accepted only for idempotent recovery. Conflicting evidence, any valid non-object JSON value including JSON `null`, or malformed in-memory JSON fails closed: the run remains `Entered`, no settlement, position, or lost-counter mutation occurs, and a bounded error identifies the run and rejection detail. Whenever the existing non-`FixedOutcomeMaker` path creates a `PaperPositionSettlement`, its source is exactly `BtcUpDown5mResolvedLedger:GammaClosedMarket`, `BtcUpDown5mResolvedLedger:MarketWebSocket`, or `BtcUpDown5mResolvedLedger:BinanceTimedClose`; `FixedOutcomeMaker` and zero-remaining-position paths retain the durable run evidence even when no position-settlement row exists. Existing fill, position, stake, fee, Gross, Net, lost-counter, and run-settlement calculations are unchanged. After deployment, the ordinary settlement worker may process eligible historical `Entered` runs through this path; there is no direct historical database update or Live behavior change.

## Paper Trading

When Paper runtime is enabled, strategy entries create `PaperOrder` records with proposed size, notional, configured TTL, copied trader wallet, and strategy id. Paper runtime is enabled in `Bot:Mode=Paper`, and can keep running as shadow Paper in `Bot:Mode=Live` when `PaperTrading:RunInLiveMode=true`. BUY and SELL entries no longer chase the current top of book in the signal decision; the pending paper order fills later only if the market trades back through that entry price. Paper BUY proposals are not rejected by opposite-outcome open orders, because Paper is used to measure strategy behavior without suppressing possible entries. When `PaperTrading:UseMinimumMarketOrderSize=true`, the proposed paper size is the current market `min_order_size` from the order book instead of bankroll-sized `$25`/`$12.50` test orders. A dedicated Paper open-order worker runs every `PaperTrading:OpenOrderProcessingIntervalSeconds`, default `5`, to expire stale pending orders, simulate approximate fills from fresh WebSocket order books first, and fall back to observed REST CLOB order books. Fill simulation is batched by `PaperTrading:OpenOrderFillSimulationBatchSize`, default `100`, so a large old backlog cannot block new GTD expiry checks. Before applying that batch cap, open orders are prioritized so expired orders are closed first, then BUY GTD opening-limit orders with initial executable ask evidence, then the earliest expiring remaining orders. If a BTC opening-limit GTD order has initial executable ask evidence, the conservative immediate-fill model is evaluated before the order is allowed to expire, so an order that was marketable in the submit snapshot is not turned into `gtd_limit_not_filled` solely because the worker reached it after its local deadline.

Immediate FAK strategy entries are evaluated from their immutable decision-time order-book snapshot, not from a later worker snapshot. The Live-shadow path submits the already validated intent without a second liquidity-book lookup. A legacy pending FAK that lacks the immutable intent/snapshot is rejected as non-reproducible and creates no new Paper fill or PnL. Until FAK fill/order/position writes are atomic, an open FAK that already has a persisted fill remains visible as a fail-safe no-op: the worker neither duplicates uncertain accounting nor terminalizes the order automatically.

For paper BUY orders, a fill is simulated only from executable ask depth at or below the paper buy limit, or from an observed trade with size at or below that limit. The engine records one `PaperFill` per simulated execution, caps each fill to the order's remaining shares, stores balanced depth/trade evidence with the VWAP, and keeps the order partially filled until cumulative fills reach the requested size. Long positions are updated with weighted-average cost and valued using the current bid, not midpoint or ask.

For paper SELL orders, a fill is simulated from executable bid depth at or above the paper sell limit, or from an observed trade with size at or above that limit. SELL fills reduce the matching copied-wallet paper position and store approximate realized PnL on the fill as `(sellPrice - averageEntryPrice) * soldShares`. The remaining position keeps the original average entry price and is marked from the current bid. When minimum-size paper orders are enabled, SELL signals with less than the market minimum remaining in the copied-wallet position are rejected as `paper_position_below_market_minimum`.

For both BUY and SELL paper fills, crossed book/trade evidence determines whether and how many shares can fill; accounting price is the submitted paper limit, not the better visible market price.

WebSocket market-data updates also dispatch into paper trading so pending orders can fill and paper positions can be re-marked without waiting for the next scanner loop. The receive path captures the exact open Paper-order ids for the updated asset; deferred fill evaluation uses only those ids and the frame receipt time, so a queued older frame cannot fill an order created later or expire an order merely because queue processing was delayed. The exposure snapshot is loaded once during service startup before the operational hosted services begin. Concurrent initial readers share that one load, and mutations routed through the cache's `Apply*` methods during a refresh are replayed over the loaded rows before publication. Before the exposure cache completes its initial load, the state is treated as unknown: coalescing is disabled and the updater performs the existing lazy load. WebSocket Paper updates read open Paper orders and positive-size positions from the shared exposure snapshot and filter them by the updated asset in memory; they do not reload the complete `paper_orders` or `paper_positions` tables for each WebSocket update. Changed marks for one asset are persisted in one PostgreSQL batch transaction before the exposure cache is updated, instead of opening one connection and issuing one upsert per copied wallet. The periodic Paper-order processor and WebSocket subscription discovery also reuse the cached positive-size position list instead of repeatedly scanning `paper_positions`; immediately before each simulated BUY or SELL fill handled by that periodic processor, accounting still reads the exact indexed `(copied_trader_wallet, asset_id)` position from PostgreSQL. Conditional settlement and copied-leader exit paths retain their dedicated positive-size database reads. Stale WebSocket snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.

Copied leader exits are tracked by a separate background worker controlled by `PaperTrading:LeaderActivityExitTrackingEnabled`. A filled copied BUY activates its `paper_copied_leader_positions` row with the actual copied paper size. The worker selects due active links, calls Data API `/activity?user=<wallet>` sorted newest first with a timestamp cache-buster, filters `TRADE`/`SELL` rows for the same asset after the copied entry, and writes deduped observations to `paper_copied_leader_activity_events`. For each matched leader sell it creates a proportional paper SELL order priced at the leader's sell activity price: `leader sell size / leader initial copied position size` is applied to our copied paper size and capped by the current available paper position minus already-open SELL orders. Activity rows with invalid prices are skipped. This is still paper-only and does not place live orders.

Paper accounting also settles copied-wallet positions when a market resolution is observed from the market WebSocket or from the periodic closed-Gamma scan. Resolution handling selects only positive-size positions matching the condition/asset, then zeroes all matching positions and writes all settlement rows atomically in one PostgreSQL transaction. Paper position upserts and conditional mark updates, including their single-row methods, plus entry/settlement batches and copied-leader exit transactions acquire deterministic transaction-scoped wallet locks before position, performance-queue, or copied-leader-position locks. Entry and settlement batches then write positions before order/fill/settlement trigger paths can acquire copied-performance queue rows; exact position keys and queue wallets are ordered deterministically. This contract prevents mark-versus-settlement, mixed insert/update trigger, and copied-leader exit lock inversions while preserving atomic rollback. Copied-trader scoring is outside this settlement path: order, fill, position, and settlement changes enqueue only their affected copied wallets at high priority, and `PaperCopiedTraderPerformanceWorker` reserves `PaperTrading:CopiedTraderPerformanceWalletBatchSize` slots per cycle for them (`25` by default). A short explicit `READ COMMITTED` claim transaction atomically moves selected pending rows into the durable `paper_copied_trader_performance_refresh_inflight` table before the expensive aggregation begins. Producers never write that in-flight table, so an order/fill/position/settlement event for a wallet being projected creates a fresh pending row instead of waiting for the whole projection transaction; a failed or interrupted cycle leaves its durable in-flight work for the next cycle to recover first. One session advisory lock serializes claim and projection across service instances. The projection separately reserves `PaperTrading:CopiedTraderPerformanceReconciliationWalletBatchSize` low-priority slots (`5` by default) for bootstrap, missed events, orphan cleanup, and later category metadata changes. The lexical seed setting remains an upper bound (`PaperTrading:CopiedTraderPerformanceReconciliationSeedWalletBatchSize`, `100` by default), but the effective seed cannot exceed reconciliation slots left after existing low-priority backlog is selected, and newly seeded wallets are processed atomically with the cursor/projection transaction. There is deliberately no capacity spill between the two classes, so default database work is capped at `30` wallets per cycle and reconciliation cannot inflate its own queue. Remaining-depth metrics include both pending and in-flight work. The first cycle runs immediately; later starts use the fixed `PaperTrading:CopiedTraderPerformanceRefreshSeconds` cadence (`30` seconds by default) rather than prior cycle duration plus delay. The worker can be disabled with `PaperTrading:CopiedTraderPerformanceProjectionEnabled`. Settlement warnings report separate open-position load, batch preparation, persistence, cache, and total durations. Do not downgrade to a binary that predates the in-flight table while it contains rows; let the new worker drain them first or transactionally requeue them before rollback.

After the final high-priority and reconciliation wallet selection, the repository analyzes its session-local selector before rebuilding the derived performance rows. This gives PostgreSQL the actual small batch cardinality so it can use the wallet-leading partial index `ix_paper_positions_open_wallet` instead of choosing full source-table scans. The `paper_positions` contribution reads only currently open rows with `size_shares > 0`; settled-position counts, outcomes, and settlement PnL continue to come from `paper_position_settlements`, while realized sell-fill PnL continues to come from `paper_fills`. Category and `OVERALL` rows are produced by one `GROUPING SETS` aggregation over the selected-wallet events rather than materializing and scanning the same event CTE twice. A settlement queries Gamma only when its stored category is null or empty; an already persisted category remains authoritative. Closed zero-size Paper positions remain stored as history, and these derived-read optimizations do not delete or compact any Paper, Live, or Live-shadow history.

The current .NET service schema intentionally does not create the unused legacy index `ix_paper_positions_wallet_updated`. Deploy and verify a build with this declaration removed before any separately approved production `DROP INDEX CONCURRENTLY`; removing the declaration alone neither drops the existing index nor changes any Paper, Live, or Live-shadow row. The production primary removed this index concurrently on `2026-08-04` after the deployment/history gate passed, reclaiming its measured `4,330,291,200` bytes without changing retained betting rows. The isolated `src4.8` schema still contains the legacy declaration and must not be deployed against the same database unless a separately reviewed rollback first recreates and validates the exact index.

Database scan diagnostics sample PostgreSQL transaction-local `paper_positions` sequential-scan counters around the copied-performance seed, copied-performance aggregate, and one-strategy Dashboard reconciliation build. Structured worker logs contain the latest phase delta, while the service heartbeat `current_loop` retains cumulative and last-positive values so a positive 30-second sample cannot be hidden by a later `0/0` before the heartbeat is persisted. Dashboard diagnostics also retain the exact strategy code for the last positive build. Treat `seq_scan` as the phase detector; `seq_tup_read` is backend-local and can be only a lower bound for a parallel scan. A transaction that fails before its after-sample is not represented by these phase totals and must be correlated with worker errors and the external table counters. These diagnostics only read PostgreSQL statistics and do not delete, compact, or rewrite Paper, Live, or Live-shadow history.

## Dry Run Trading

In `DryRun` mode, accepted signals produce CLOB V2 order payloads without sending them to Polymarket. The dry-run path validates tick size, minimum size, price, signature type, signer/funder addresses, order type, and GTD expiration. BUY and SELL amounts are converted with 6-decimal fixed math according to the official V2 order model.

When `PolymarketAuth:DryRunSigningEnabled` is true and `DryRunPrivateKeyName` resolves through the configured secret provider, the app signs the order locally with the V2 EIP-712 domain. If the key is absent, the signer address does not match, or validation fails, the result is stored as `DryRunUnsigned` or `DryRunRejected`. Stored payloads are redacted and no `POST /order`, cancel, or authenticated trading HTTP call is made.

## Live Trading

Live trading is disabled by default. To place any live order, all gates must pass: `Bot:Mode` must be `Live`, `Bot:EnableLiveTrading` must be `true`, `LiveTrading:ManualEnableCode` must equal `LIVE_TRADING_ENABLED`, the strategy `Live` flag must be on and not auto-Live-paused, auth must be configured, geoblock must be clear from the machine running the service, CLOB server time must be within drift limits, no API-error or daily-loss lockout may be active, and the local kill switch/live pause must be clear. `PaperTrading:RunInLiveMode=true` only keeps Paper simulation and settlement running alongside Live; it does not place or authorize live orders.

Paper/Live-shadow Paper rows may still use their strategy-specific Paper model, including GTD simulation, but every live-shadow submission now sends a BUY `FAK` market amount with `postOnly=false` and no GTD expiration. A zero-fill FAK response is stored as a rejected live entry instead of an open order. GTD local cancellation and `ClobGtdExpirationSecurityBufferSeconds` still apply to Paper GTD simulation and legacy open GTD live-order maintenance, not to new live submissions. Checking a strategy's Dashboard `Live` checkbox sets `strategies.live_stakes=true` and makes that opening-limit strategy eligible for new live-shadow orders when all live gates pass; unchecking it sets `strategies.live_stakes=false` and stops new live-shadow entries without a service redeploy. Operational CLI toggles can still enable exactly one strategy with `--set-live-stakes-only-code <code>` or exactly a set of strategies with `--set-live-stakes-only-codes code1,code2`; both forms intentionally disable Live for every other strategy. Before placement the service refetches the order book, checks clock drift and risk caps, blocks any Live BUY when the same condition already has an open Live BUY on a different outcome, enforces live bankroll/strategy-balance caps, signs the CLOB V2 payload locally, and sends `POST /order` with L2 headers. `LiveTrading:MaxOrderNotionalUsd` is a hard emergency ceiling rather than the normal stake-sizing control; intended per-strategy sizing is controlled through Dashboard `Live $`, optional `Live Lost` loss-counter add-on, and `Live bal`. Live preflight applies `LiveTrading` market/total exposure caps to open Live orders only; Paper backlog stays governed by Paper controls and does not consume Live safety ceilings. Paper/Live-shadow Paper sizing can include the `Paper Lost` loss-counter add-on, while Live sizing can include the separate `Live Lost` loss-counter add-on except for stats-probe variants. Live orders and live events are stored in PostgreSQL; the maintenance loop polls order status and cancels expired/stale legacy open orders. Paper/Live-shadow maintenance requires the linked Paper and Live order shape to match on asset, condition, outcome, expected live order type `FAK`, expected `postOnly=false`, and limit/worst price within `0.000001`. Requested size is intentionally allowed to differ because Paper and Live stake sizing are separate. Shape mismatches record a discrepancy, disable that strategy's `LiveStakes`, and cancel correlated open live orders. The kill switch pauses new live orders and requests cancel-all.

Each strategy also has a live-only `live_available_balance`, default `100.00`, visible and editable as `Live bal` in the Dashboard `Strategies` tab. Live preflight treats existing open live orders for the same strategy as reserved notional. If the remaining strategy balance cannot cover the next live stake, the service logs an error, writes a `StrategyLiveBalance` live event, flips that strategy's `LiveStakes` flag off, and stops placing live orders for that strategy even if the system-wide live caps would still allow trading. Live orders persist accounting fields for average fill price, filled notional, cost basis, fee, settlement value, gross and net realized PnL, won/lost flag, and settlement source. For immediately matched CLOB submit responses, BUY accounting derives actual fill price from `makingAmount / takingAmount`, so the submitted worst price is treated as a cap rather than the realized execution price. Aggregate Polymarket Data API positions are recorded only as observations for Live FAK orders; they do not create per-order fills, Paper-shadow fills, or realized PnL because they are not exact order-level execution data. When a matched live order can be resolved from closed Gamma metadata, the maintenance loop applies fee-accounted net live PnL exactly once and clamps the stored balance to the `0.00` to `100.00` range. If fee accounting is unavailable, resolution fields may be retained but the balance effect stays pending and gross PnL is not substituted as net. This accounting does not affect Paper trading balances; Dashboard Paper and Live strategy metrics remain separate.

On startup the service checks Polymarket geoblock status from the actual host and writes a `StartupGeoblockCheck` live event. A successful response with `blocked=true` always pauses or blocks live trading. If the endpoint itself fails, `LiveTrading:BlockOnGeoblockCheckFailure` controls the behavior: `true` pauses/blocks live, while `false` records a `GeoblockCheck` warning and lets the remaining live gates decide.

The Dashboard `Live Readiness` tab shows the current live blockers in one place: config gates, auth readiness, latest dry-run signed order, startup geoblock event, IPC service state, live pause, kill switch, open/stale live orders, API-error and daily-loss lockouts, strategy live-stake funding, and market WebSocket status. It is read-only and does not enable live trading or place orders.

## Fee Accounting

New Paper fills and filled Live orders retain platform-fee provenance separately from the existing PnL fields. The service reads the market-specific CLOB V2 schedule from `GET /clob-markets/{condition_id}`: `fd.r` is the rate, `fd.e` is the integer price-curve exponent, and `fd.to` says whether the schedule applies only to takers. A modeled fill fee is `shares * rate * (price * (1 - price))^exponent`, rounded deterministically to five decimal places. An absent `fd` is treated as fee-free only when both CLOB base-fee fields are explicitly present and zero; missing, incomplete, or invalid evidence is not guessed.

Liquidity role is stored as `Maker`, `Taker`, or `Unknown`. Post-only/resting execution is Maker, FAK/FOK execution is Taker, and ambiguous or contradictory evidence remains Unknown. A known Maker under a taker-only schedule has a calculated zero platform fee. An Unknown role under a non-zero applicable schedule produces `CalculationUnavailable`, not a zero-fee assumption.

Fee coverage is explicit: `LegacyUnknown` means the row was never evaluated with the current fee model; `CalculationUnavailable` means evaluation was attempted but required schedule, role, or fill evidence was unavailable or invalid; `Calculated` is the deterministic local model; `VenueReported` is reserved for an authoritative venue-reported fee; and `PartiallyCalculated` marks an aggregate containing both accounted and unaccounted children. A locally modeled result is always `Calculated`, never `VenueReported`.

Existing `RealizedPnlUsd`, `UnrealizedPnlUsd`, and gross ROI fields remain unchanged as secondary audit values before platform fees. The Dashboard presents nullable net realized, open, mark-to-market, closed ROI, and Live metrics as the primary strategy results only when every contributing fee is `Calculated` or `VenueReported`. Partial, unavailable, and legacy coverage leaves the corresponding net cell blank instead of substituting zero; adjacent `accounted/required` coverage identifies completeness, with an empty `0/0` scope shown as `N/A`. `AccountedFeeUsd` is the known accounted fee sum and is not a claim that missing fees are zero. Net ROI uses fee-inclusive cash outlay as its denominator: gross stake or cost plus the fully accounted platform fee. CSV exports place the nullable net values, known fee, and coverage before the retained explicitly named `Gross...` audit columns.

`PaperFakFeeBackfill` performs the retained historical pure-Paper FAK migration online. It uses a fixed pre-fee-deployment cutoff, scans only BUY fills whose persisted execution source is exactly `btc_updown5m_fak_taker_paper` or `btc_updown5m_child_mirror_fak_paper`, forces the proven `Taker` role, and evaluates them through the same current fee calculator used for new fills. At the start of each sweep, the worker reads the materialized lifetime `Gross realized (audit)` displayed by the Dashboard and freezes strategies with either historical FAK-source orders or an eligible unresolved Settled Paper run in descending Gross order, breaking ties by strategy ID. If a source strategy does not yet have a Dashboard snapshot, the rank query falls back to the same retained run/fill/settlement Gross formula. Exact `LegacyUnknown`, cutoff, BUY, and source eligibility is then enforced by the strategy-bound exact candidate page, avoiding a multi-million-row global fill/order join before every sweep. The worker finishes the exact phase for one strategy before its run-level repair/fallback phase and then moves to the next, so the most successful strategy is attempted first and normally reaches complete Net PnL and Net ROI coverage first. This rank changes scheduling only: Gross values and the Dashboard aggregate Gross/Net PnL and Net ROI formulas are unchanged, while the exact phase retains its cutoff, source allowlist, candidate filters, and financial formula. The default cadence is one batch of 50 rows every 15 seconds after a five-minute startup delay. Pending Paper-entry persistence and market-data side effects receive one full cycle first, after which one bounded backfill batch may run before yielding again; this prevents continuously nonempty foreground queues from starving the migration forever. At the previewed `2,196,391`-fill scale, the no-contention lower bound is about 7.6 days, while continuously pending foreground work makes the bounded schedule about 15.2 days. Each short apply is atomic, conditional, and idempotent on retry. Gross fields and their timestamps are not rewritten; exact calculated fee provenance is prefixed with `historical-current-paper-model-v1`.

After the exact phase, every historical or future `Settled` Paper run with positive stake and non-null Gross is eligible for the separate canonical-run phase, regardless of execution source or historical cutoff; Live accounting and execution are not changed. A nonnegative Fee already marked `Calculated` or `VenueReported` is repaired exactly first by setting a missing or inconsistent Net to `Gross - Fee` while preserving Fee, status, source, and fee metadata. For a run still incomplete after exact repair, each bounded transaction recomputes the lifetime same-strategy coefficient `R = SUM(exact Fee) / SUM(exact positive Stake)` from complete `Calculated` or `VenueReported` runs satisfying `Net = Gross - Fee`. Prior ratio-finalized runs are excluded. Without a valid donor or positive aggregate donor stake, the run remains unchanged for a later ranked visit.

The approximate fallback stores `Fee = ROUND(Stake * R, 8)` and `Net = Gross - Fee` on the canonical strategy run only, marks it ordinary `Calculated`, and uses the exact case-sensitive source `strategy-settled-fee-stake-ratio-v1`. It adds no visible `Estimated` status or label and never revisits or replaces a successfully finalized run when donors change or exact fee evidence later becomes available. Related fill, order, position, and settlement rows may therefore retain earlier statuses or blank Net in detailed exports even though run-backed strategy Net PnL and Net ROI become complete. Lock/query deferrals, transport or programming failures, service cancellation, and a concurrent exact completion never create or overwrite a financial estimate; they leave the applicable work unchanged for retry. Gross ordering, Paper/Live execution semantics, risk gates, and Live accounting remain unaffected.

`HistoricalGrossNetParity` closes the remaining pre-fee history for originating
entries strictly before `2026-08-10T00:00:00Z`. It targets exactly the canonical
contributions already selected by Gross: Settled Paper runs, positive open Paper
positions, the existing runless settlement/SELL fallback, and counted settled
Live orders. Gross-excluded rows are also excluded from Net coverage and never
block the strategy. Gross values, bases, execution, fills, and settlement facts
are not changed.

The workflow preserves exact accounting first, then tries the existing exact
CLOB model, then uses a deterministic exact-donor Fee-to-Gross-basis ratio from
the same or nearest typed strategy, falling through to any proved crypto donor.
If every donor tier is empty it uses `R=0.0333`. It stores
`Fee = ROUND_AWAY_8(B * R)` and `Net = Gross - Fee` as
ordinary terminal `Calculated`, excluding all estimated rows from future donor
pools. Live prefers already-associated `VenueReported` evidence and does not add
an on-chain fee matcher. Its historical balance correction is audited and does
not toggle Live or modify loss counters.

With `HistoricalGrossNetParity:Enabled=true`, the service does the work itself
in bounded background cycles. It first exhausts the current exact/authoritative/
local-calculation pass. It then selects unresolved old targets in Gross order,
queries only the finite strategy candidates required by the deterministic donor
tiers, calculates the target-time exact aggregate, and immediately applies that
one decision through compare-and-set/serializable storage. There is no global
donor scan or frozen donor universe, complete database plan, file artifact,
`ApplyEnabled` switch, digest command, or second approval after deployment.

Each target stores the selected donor strategy and tier, exact numerator and
denominator, counts, deterministic membership/selection hashes, ratio, basis,
Fee, Net, and provenance. A concurrent target or donor change rolls back that
target for a later cycle without undoing independent completed targets. Different
targets may legitimately see different exact donor membership as the service
progresses; a terminal Paper estimate is not recalculated for that reason.
Restart simply rescans unresolved or Pending canonical rows and durable audit.
For historical Live balance application, the earliest unfinished order gates
only later initial balance effects of the same strategy; accounting and other
strategies continue. Dashboard visibility still waits for the applicable cycle
and projection reconciliation.

Each strategy-bound candidate page first materializes that strategy's exact
allowlisted BUY order IDs, probes their fills through the existing per-order
index, and sorts and limits only the strategy-local candidate keys before loading
the full rows. This avoids scanning the global chronological `LegacyUnknown` fill
index to discover the current strategy and does not change eligibility, keyset
ordering, batch size, or financial calculations.

The dedicated PostgreSQL table `paper_fak_fee_backfill_events` stores only the
structured lifecycle, strategy-ranking, cycle, and failure events emitted by
this backfill worker. The existing rolling file log remains the fallback when
PostgreSQL is unavailable. Event retention is an intentionally fixed contract:
rows strictly older than 24 hours are removed every 10 minutes, up to the 500
oldest rows per cleanup cycle. The table is not a reconstruction of earlier
file logs; it contains only events emitted after the database-event feature is
deployed. Recent events can be inspected directly:

```sql
SELECT occurred_at_utc, level, event_type, message,
       worker_instance_id, sweep_id, cycle_id
FROM paper_fak_fee_backfill_events
WHERE occurred_at_utc >= now() - interval '24 hours'
ORDER BY occurred_at_utc DESC, id DESC;
```

The conditional apply accepts exactly two dependency shapes. `FullChain` requires the unchanged exact fill/run/zero-size-position/settlement chain and accepts either settlement source `BtcUpDown5mGammaClosedMarket` at exactly the run settlement time or exact `MarketWebSocket` with settlement time at or before the run settlement time; every identity, economic, uniqueness, and accounting guard remains mandatory, and the apply updates fill, run, position, and settlement fee/net fields. `RunOnlyLegacy` requires exactly one unchanged, settled, economically self-consistent run and no position or settlement rows; it updates only the fill and run and never synthesizes missing accounting rows.

The historical worker deliberately excludes GTD, Maker, ambiguous sources, already-accounted rows, and every `paper_live_shadow_actual_fill`. A Live-shadow fee belongs to the linked aggregate Live execution and is copied into Paper by the normal reconciliation path; rewriting only its Paper row would make Paper and Live accounting disagree. Item-level structural or accounting conflicts leave the row unchanged and advance the cursor after the completed SQL batch; they can be reconsidered in a later ranked sweep. A whole-batch lock timeout or query cancellation leaves the cursor unchanged so the same page is retried. Temporary CLOB lookup failures are never persisted as zero fees. `ReachedEnd` means only that one keyset sweep ended; it does not prove that no conflicting, deferred, or unavailable legacy rows remain.

## Analytics And Reporting

The service automatically generates daily reports into `daily_reports` when `Analytics:DailyReportGenerationEnabled` is true. Reports are recalculated every `Analytics:DailyReportRefreshMinutes` for the current UTC day and the previous UTC day.

Dashboard analytics include:

- daily summary: signals observed/accepted/rejected, paper orders, fills, expired orders, paper PnL, open paper exposure, top rejection reasons, API errors;
- trader performance: signal counts, acceptance rate, fill rate, average lag, leader/proposed price comparison, approximate paper PnL, rejection reasons;
- category performance: grouped by `markets.category`, or `unknown` when category is not available;
- date-dependent strategy hourly Paper PnL: `date_dependent_strategy_hourly_paper_pnl` keeps 24 UTC-hour rows per configured strategy, currently only `SOL Up or Down 5m Down 8 bps Reference Average Premarket`, refreshed from settled Paper strategy runs after each UTC hour;
- execution quality: leader price, proposed price, fill price, price deltas, lag/spread, and bid/ask/mid snapshots after 1m, 5m, and 30m when stored market data exists;
- rejection analysis: reason code counts and share of rejected signals.

CSV export from the dashboard writes `LeaderTrades.csv`, `Signals.csv`, `SignalRejections.csv`, `PaperOrders.csv`, `PaperPositions.csv`, `PaperCopiedTraderPerformance.csv`, `Strategies.csv`, `StrategyRecentPerformance.csv`, `OnChainTrades.csv`, `OnChainParticipants.csv`, and `DailyReports.csv` under `Analytics:CsvExportDirectory`. The `Dashboard Errors` tab has a separate `Save errors` action that writes the current in-memory error buffer to `DashboardErrors.csv` under a timestamped `*-dashboard-errors` export folder.

Interpret paper results conservatively. Paper fills are approximate, long positions are marked from bid-side data, and historical daily PnL is a generated snapshot over stored paper positions rather than broker-grade accounting. Use the reports to compare filters, traders, categories, and execution quality before considering any live-trading work.

## Dashboard Screens

- The default `StrategiesOnlyMode=true` screen shows the strategy tabs, strategy-linked `Paper orders` / `Live orders` tabs, the local `Dashboard Errors` tab, database selector, service banner, refresh button, and strategy edit controls.
- Strategies: all configured strategies with editable enable/live-stake/live-balance controls, per-row Paper/Live order navigation, Paper orders, open positions, lifecycle runs, entry-delay health, wins/losses, PnL, live-snapshot ROI, split live skip/ignored counts with ignored-reason breakdown, and closed-only ROI.
- Strategies / 24 hours, 6 hours, 1 hour: short strategy slices with per-row Paper/Live order navigation, recent order/fill/expiry/settlement counts, ROI, average fill price, entry-delay health, split live skip/ignored counts with ignored-reason breakdown, and top skip reason.
- Full mode (`Dashboard:StrategiesOnlyMode=false`) also shows the legacy operational tabs:
- Overview: service heartbeat, mode, storage/API status, scanner status, bankroll, exposure, PnL.
- Watchlist: configured traders plus scanner counters and errors.
- Trader Discovery: leaderboard best/worst PnL candidates enriched with recent trades and positions.
- Onchain Trades: enriched decoded `OrderFilled` rows with market/outcome metadata, maker/taker participants, side, price, size, fee, and transaction hash.
- Onchain Participants: one-row-per-wallet participant summary with executions, buy/sell counts, positions, exposure, resolved PnL, ROI, win rate, score, and activity window.
- Onchain Leaders: first-pass wallet score based on materialized positions, resolved PnL, ROI, win rate, sample quality, volume, and open exposure.
- Onchain Rankings: activity ranking built from materialized wallet activity over normalized wallet executions.
- Onchain Positions: wallet positions aggregated by market token/outcome with net shares, net cost, and resolved PnL where available.
- Onchain Executions: recent wallet-level on-chain executions with wallet, token id, side, average price, size, notional, contract, and tx hash.
- Leader Trades: latest observed leader trades.
- Signals: accepted/rejected decisions, reason codes, proposed paper details.
- Dry Run Orders: unsigned/signed/rejected dry-run payload records and validation messages.
- Live orders: submitted/live/rejected/cancelled live order records with a strategy selector defaulting to all strategies. Opening this tab from a recent strategy grid keeps that period window, such as last 6 hours, and Live paging remains inside the window.
- Live Events: live placement, cancellation, polling, and error audit entries.
- Live Readiness: read-only live-session gate checklist showing blockers before any live order can be considered.
- Paper orders: lifecycle, TTL, fill timestamps, linked signal id, settlement value, realized PnL, settled time, inferred winning outcome, `Won`, and a strategy selector defaulting to all strategies. Opening this tab from a recent strategy grid keeps that period window, such as last 6 hours.
- Paper Positions: size, average price, estimated value, unrealized PnL.
- Copied Ratings: per copied wallet/category Paper performance used to evaluate followed leaders.
- Market Data: latest WebSocket/market-data asset snapshots, bid, ask, spread, update time.
- Analytics: daily, trader, category, execution-quality, and rejection reports.
- Risk: configured limits and current usage.
- Diagnostics: sanitized config summary, storage status, auth status, service/scanner/WebSocket status, watchlist summary, latest API errors, and risk usage.
- Runbook: local paths and purposes for the operations documents.
- Logs: API errors, risk events, service commands, and market-data events.
- Dashboard Errors: local dashboard refresh, IPC command, CSV export, and strategy edit errors retained in memory with wrapped details, selected-row copy, and `Save errors` export. This tab is visible in both strategies-only and full modes.
- Controls: pause/resume scanner, pause/resume paper/live trading, kill switch, clear kill switch, cancel all live orders, trader discovery, on-chain sync/cancel, on-chain market enrichment, and asset pin/unpin through localhost IPC.
- Trader discovery refresh is also a localhost IPC command and only runs when the operator presses the dashboard button.

## Troubleshooting

- PostgreSQL not configured: set `POLYCOPYTRADER_POSTGRES_CONNECTION` and restart the service. The service does not run with no-op storage.
- Invalid watchlist wallet: the scanner skips placeholder/invalid wallets, records a warning status, and keeps the service running.
- Dashboard `NpgsqlException: Exception while reading from stream` during strategy refresh means PostgreSQL did not finish the strategy-performance aggregate before the query timeout. Strategy Dashboard queries use an explicit 180-second command timeout, and the schema includes Dashboard support indexes for recent strategy windows and paper fill/order joins. If this still appears, inspect PostgreSQL load/table bloat and move all-time strategy-performance aggregation to a precomputed cache before relying on remote full-grid refreshes.
- Polymarket TLS certificate errors: configure `Polymarket:CertificatePins` only after verifying the current endpoint certificate pin out of band. Do not use an accept-any certificate callback in production.
- HTTP 429/5xx from Polymarket: public clients retry transient failures according to `Polymarket:MaxRetries` with exponential backoff from `Polymarket:RetryBaseDelayMilliseconds`, default `1000`, and record API errors when retries are exhausted.
- Gamma active-market `HTTP 422` max-offset responses such as `offset exceeds maximum allowed` or `offset too large, use /markets/keyset` are treated as the normal end of the legacy offset scan and are not recorded as API errors.
- Malformed API response: the failing operation is recorded as an API error; scanner/signal/paper loops continue on later cycles.
- WebSocket disconnected/stale: the market WebSocket reconnects with backoff and stale snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.
- Polygon RPC rejects `eth_getLogs`: lower `OnChainIngestion:MaxBlockRange` or configure `POLYCOPYTRADER_POLYGON_RPC_URL` for a more reliable provider. Public/free RPC endpoints commonly require ranges at or below `10000` blocks; the default is `500`.
- IPC unavailable: check whether `http://127.0.0.1:5118/` is already in use, then run `GET /health` or the QA script runtime smoke.
- Database temporarily unavailable: loop-level error recording is best-effort and will not crash the worker if error persistence also fails.
- VPS backup failing: confirm PostgreSQL client tools are installed and `POLYCOPYTRADER_POSTGRES_CONNECTION` is available to the scheduled task or service account.

Do not enable live trading unless `dotnet build`, `dotnet test`, `--print-config`, runtime IPC smoke, geoblock check from the actual host, and cancel-all testing pass.

## Known Limitations

- A Paper depth sweep may be persisted as one aggregate fill at VWAP. Because the fee curve is nonlinear, applying it once to aggregate shares and VWAP can differ from summing independently rounded fees for each actual match; exact accounting requires per-match fills or a venue-reported fee.
- `Calculated` fees are a versioned five-decimal local model, not `VenueReported` amounts. The public schedule and formula do not independently prove the venue's midpoint-tie rounding behavior, so modeled values retain their model status.
- Maker rebates and builder-attribution fees are excluded from current fee/net PnL accounting. They require separate authoritative payout or builder-fee evidence and must not be represented as negative platform fees or silently folded into the CLOB schedule.
- Replacing a legacy Paper/Live-shadow fill inside a position that also contains non-shadow size cannot reconstruct the pre-shadow fee provenance because old positions have no versioned component snapshot. Such mixed legacy aggregates remain conservatively `PartiallyCalculated`/`Unknown` with nullable net PnL; they are not promoted to fully accounted from numeric subtraction alone.
- `SOL Up or Down 5m 1 Diff Up Progress` and `SOL Up or Down 5m 2 Diff Up Progress` are retired: they are no longer seeded and their production history was removed.
- API credential bootstrap currently supports Windows Credential Manager storage only.
- Trader enable/disable and cancel selected paper order dashboard buttons are placeholders until command-specific IPC is added.
- On-chain leader scoring is a transparent first pass over resolved positions; it has no current mark-to-market yet.
- The on-chain paper-signal worker depends on timely Gamma market rows, category mappings, Polymarket rating refreshes, and Polygon block polling for fast paper BUY entries. Copied exits depend on Data API `/activity`; if that endpoint lags or omits rows, the paper exit may also lag.
- User-authenticated WebSocket channel is not implemented yet.
- The market-data queue limits are intentionally soft for trade, resolution, open-order, parse-error, and other important diagnostic events; sustained downstream database stalls can therefore grow non-replaceable backlog. Monitor the periodic side-effect queue metrics after deployment.
- Batched mark and settlement upserts still fire the existing row-level Dashboard projection triggers. Batching removes per-wallet connections and transactions, but the remaining trigger/event lock time and projection-event volume must be measured after deployment before changing projection delivery semantics.

## Next Recommended Task

After deployment, compare settlement `LoadDurationMs` / `PersistenceDurationMs` / `TotalDurationMs`, Paper-position lock waits, projection-event volume, side-effect queue delay, and copied-trader performance refresh cadence across several busy five-minute boundaries. Independently continue diagnosing remote critical-WebSocket closes; batching does not establish or imply their cause.

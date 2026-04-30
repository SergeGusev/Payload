# PolyCopyTrader Project Memory

Last updated: 2026-04-30, Europe/Sofia.

This file is the high-context memory note for a future Codex session. If the user asks
"what did we build?" or "continue the PolyCopyTrader project", read this file first,
then read `README.md`, `AGENTS.md`, and the operational docs under `docs/`.

Do not store secrets in this repository. Real PostgreSQL passwords, API keys, private
keys, wallet secrets, and Polymarket credentials must stay in environment variables,
Windows Credential Manager, or the user's password manager.

## One-Sentence Summary

PolyCopyTrader is a cautious C#/.NET Windows copy-signal system for Polymarket. It
observes selected leader wallets, filters their trades through category/freshness/
spread/liquidity/risk checks, simulates paper maker orders, supports dry-run CLOB V2
signing, and contains heavily gated tiny maker-only live order plumbing that is
disabled by default.

## Current State

All numbered Codex tasks `01` through `18` are implemented.

The repository also has local debugging and trader discovery support after task completion:

- local PostgreSQL debugging scripts;
- optional Docker Compose PostgreSQL fallback;
- dashboard environment-variable config support;
- local Windows PostgreSQL database `polycopytrader` created and initialized;
- service now requires configured PostgreSQL storage and fails fast without it;
- dashboard `Find traders` now runs deep trader discovery;
- trader discovery dashboard button and tab for best/worst PnL candidates;
- Polymarket certificate pinning for HTTP clients and the market WebSocket;
- Polymarket HTTP request/response audit table;
- background on-chain ingestion, market enrichment, activity refresh, position
  refresh, and wallet performance workers.

Latest verified code state on 2026-04-30:

- branch: `master`;
- latest commit before this memory refresh: `8519b5e Merge trader leaderboard snapshots by wallet`;
- working tree was clean before this memory refresh;
- `dotnet test tests\PolyCopyTrader.Tests\PolyCopyTrader.Tests.csproj -c Verify --no-restore`
  passed: 119/119 after on-chain leaders work;
- `dotnet build src\PolyCopyTrader.Service\PolyCopyTrader.Service.csproj -c Verify --no-restore`
  passed with 0 warnings and 0 errors;
- `dotnet build src\PolyCopyTrader.Dashboard\PolyCopyTrader.Dashboard.csproj -c Verify --no-restore`
  passed with 0 warnings and 0 errors;
- local PostgreSQL schema initializer test passed against the user's local database;
- live trading remained disabled.

After this note was originally created, trader discovery was added:

- config section: `TraderDiscovery`;
- tables: `trader_discovery_candidates` and later `trader_leaderboard_snapshots`;
- service processor and manual IPC command: `src/PolyCopyTrader.Service/TraderDiscovery`;
- dashboard tab: Trader Discovery;
- tests increased from 91 to 93.

Later, Polymarket certificate pinning was added:

- config field: `Polymarket:CertificatePins`;
- pin format: `sha256/<base64 SPKI SHA-256>`;
- supported for development and production;
- used by Data API, CLOB API, geoblock, trading HTTP clients, and market WebSocket;
- hosts must match configured Polymarket endpoint hosts;
- host without configured pins still uses standard .NET TLS validation;
- host with configured pins accepts only a matching, currently valid certificate key.
- tests increased from 93 to 100.

Script `scripts/get-polymarket-certificate-pins.ps1` prints the current SPKI pins and
can output an appsettings fragment with `-AsAppSettings`. Always inspect printed
certificate Subject/Issuer before trusting a pin. On 2026-04-29 the user's local
network presented an `a1hosting.bg` certificate for Polymarket hosts, which explains
`RemoteCertificateNameMismatch`; that is not a Polymarket certificate.

Later, Polymarket HTTP request/response logging was added:

- table: `polymarket_http_logs`;
- domain record: `PolymarketHttpLogEntry`;
- repository methods: `AddPolymarketHttpLogAsync` and `GetRecentPolymarketHttpLogsAsync`;
- sink interface: `IPolymarketHttpLogSink`;
- service implementation: `RepositoryPolymarketHttpLogSink`;
- public Data API, CLOB public API, geoblock, and authenticated trading HTTP calls
  write one row per actual HTTP attempt;
- rows include component, operation, method, full request URL, request/response UTC
  timestamps, duration, attempt number, status code, success flag, response body
  preview, and error message;
- request bodies and auth headers are intentionally not stored.
- tests increased from 100 to 102.

Later, manual on-chain ingestion was added:

- config section: `OnChainIngestion`;
- manual IPC command: `POST /refresh-onchain` (`/refresh-onchain-7d` remains accepted as a compatibility alias);
- manual IPC command: `POST /refresh-onchain-markets`;
- dashboard buttons: `Onchain sync` and `Enrich markets`;
- dashboard tabs: `Onchain Leaders`, `Onchain Rankings`, `Onchain Positions`, and
  `Onchain Executions`;
- hosted services: `OnChainIngestionWorker`, `OnChainMarketEnrichmentWorker`,
  `OnChainActivityRefreshWorker`, `OnChainPositionRefreshWorker`, and
  `OnChainPerformanceRefreshWorker`;
- tables: `polymarket_onchain_logs`, `polymarket_onchain_fills`,
  `polymarket_onchain_wallet_fills`,
  `polymarket_onchain_wallet_executions`,
  `polymarket_onchain_token_metadata`,
  `polymarket_onchain_wallet_activity`,
  `polymarket_onchain_wallet_activity_refresh_queue`,
  `polymarket_onchain_wallet_positions`,
  `polymarket_onchain_position_refresh_queue`, and
  `polymarket_onchain_wallet_performance`,
  `polymarket_onchain_wallet_performance_refresh_queue`, and
  `polymarket_onchain_ingest_cursors`;
- Polygon JSON-RPC client reads `eth_getLogs` from configured V1/V2 CTF Exchange
  and Neg Risk CTF Exchange contracts;
- V1/V2 `OrderFilled` events are decoded into wallet, side, token id, price, size,
  notional, fee, contract, and transaction hash fields;
- each run catches up fresh blocks first, then backfills older history down to
  `OnChainIngestion:HistoricalBackfillStartUtc` (`2025-10-30T00:00:00Z` by
  default);
- cursor `to_block` is the newest completed block, cursor `from_block` is the
  oldest completed block, so cancelling and restarting resumes after the last
  completed batch;
- background ingestion catches up fresh blocks first, then processes at most
  `BackgroundHistoricalBatchesPerCycle` historical batches round-robin across
  contracts before sleeping and checking fresh blocks again;
- raw fills are normalized into maker/taker wallet rows, then aggregated into
  wallet executions by wallet, transaction hash, token id, and side;
- if raw fills already exist without wallet derived rows, the next on-chain sync
  rebuilds the missing derived range from PostgreSQL before reading more RPC data;
- wallet activity ranking is materialized in `polymarket_onchain_wallet_activity`;
  derived-data rebuilds enqueue affected wallets into
  `polymarket_onchain_wallet_activity_refresh_queue`, then
  `OnChainActivityRefreshWorker` refreshes execution count, buy/sell counts,
  distinct token count, volume, fees, activity score, and first/last trade time
  in wallet batches. `Onchain Rankings` reads this table instead of grouping the
  full wallet execution table during every dashboard refresh;
- market enrichment fetches missing execution token ids from Gamma
  `markets?clob_token_ids=...`, stores market/outcome/category/status metadata,
  writes not-found markers for unresolved tokens, and repeats batches until no
  missing tokens remain or `MarketEnrichmentMaxBatchesPerRun` is reached;
- background market enrichment runs the same processor every
  `MarketEnrichmentIntervalSeconds`;
- both background workers record transient failures in `api_errors` and retry
  with exponential backoff from `BackgroundErrorDelaySeconds` to
  `BackgroundMaxErrorDelaySeconds`;
- wallet positions aggregate executions by wallet, token, market, and outcome,
  exposing buy/sell shares, net shares, net cost, average buy/sell price, volume,
  and resolved PnL when Gamma metadata identifies the winning outcome;
- positions are a materialized table, not a live SQL view; ingestion, derived
  rebuilds, Gamma enrichment, and an initial missing-token seed enqueue token ids
  into `polymarket_onchain_position_refresh_queue`, then
  `OnChainPositionRefreshWorker` refreshes `polymarket_onchain_wallet_positions`
  in token batches;
- wallet performance is also materialized; position refreshes enqueue affected
  wallets into `polymarket_onchain_wallet_performance_refresh_queue`, then
  `OnChainPerformanceRefreshWorker` refreshes
  `polymarket_onchain_wallet_performance` in wallet batches;
- `Onchain Rankings` remains activity-based but is served from the materialized
  wallet activity table, while `Onchain Leaders` is a first heuristic performance
  score over materialized positions, resolved PnL, ROI, win rate, sample quality,
  volume, and open exposure. It has no current mark-to-market yet.
- tests increased from 102 to 119.

Later, `PolyCopyTrader.Service` was changed to require PostgreSQL storage on every
real service run. If `POLYCOPYTRADER_POSTGRES_CONNECTION` or `Storage:ConnectionString`
is missing, the service fails on startup instead of registering `NoOpAppRepository`.
This prevents Polymarket HTTP logs and other audit rows from silently disappearing.
The dashboard can still use `NoOpAppRepository` for empty diagnostic startup.

The Windows User environment variable `POLYCOPYTRADER_POSTGRES_CONNECTION` was set
locally for the user's machine. Do not copy its value into repository files. Visual
Studio must be restarted after changing that variable because debug processes inherit
environment variables from the Visual Studio process.

## Important Safety Position

Live trading exists in code, but it is not ready to turn on casually.

The default posture is still paper-first:

- no live trading by default;
- no real secrets in repo;
- no appsettings secrets;
- no funded test private keys;
- no live orders unless every safety gate passes;
- always re-check current official Polymarket docs before modifying auth/trading code.

The live path requires all of these gates:

- `Bot:Mode=Live`;
- `Bot:EnableLiveTrading=true`;
- `LiveTrading:ManualEnableCode=LIVE_TRADING_ENABLED`;
- `PolymarketAuth:Enabled=true`;
- valid signer and funder addresses;
- order signing key available through configured secret provider;
- L2 API credentials available through configured secret provider;
- startup/current geoblock check clear from the host;
- CLOB server time drift within `LiveTrading:MaxClockDriftSeconds`;
- no local kill switch;
- live trading not paused;
- no API-error lockout;
- no daily-loss/risk lockout;
- open live order count below cap;
- BUY-only;
- GTD-only;
- post-only/maker-only;
- tiny notional cap;
- maker price must not cross the order book;
- no broad crypto/sports text matches in the initial live guardrails.

## Local PostgreSQL Status

The user has PostgreSQL installed locally and sees it through pgAdmin 4.

On 2026-04-29 we confirmed:

- PostgreSQL listens on `127.0.0.1:5432`;
- server version reported by connection test: PostgreSQL 17.5 on Windows;
- connection as user `postgres` worked with a password supplied by the user in chat;
- database `polycopytrader` did not exist and was created;
- the app initialized schema in that database;
- public schema had 24 tables at the first local PostgreSQL verification; after later
  trader discovery, leaderboard snapshot, and Polymarket HTTP logging changes, the
  schema initializer defines 36 tables.

Do not write the local password to files. Use a shell environment variable or pass a
connection string at runtime.

Recommended local connection-string shape:

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=127.0.0.1;Port=5432;Database=polycopytrader;Username=postgres;Password=<local-password>;SSL Mode=Disable;Include Error Detail=true"
```

Run local service against the installed PostgreSQL:

```powershell
.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase
```

Run dashboard in a second terminal with the same env var:

```powershell
.\scripts\run-local-dashboard.ps1 -NoPostgres
```

Docker note:

- `docker-compose.local.yml` exists as a fallback for machines without PostgreSQL.
- It binds a PostgreSQL container to `127.0.0.1:54328`.
- The Docker container was stopped after the user's local PostgreSQL was confirmed.
- A Docker volume may still exist unless explicitly removed with
  `.\scripts\stop-local-postgres.ps1 -DeleteData`.

## PostgreSQL Tables

The schema initializer created these tables at the time of verification:

- `api_errors`
- `bot_settings`
- `daily_reports`
- `dry_run_orders`
- `leader_positions`
- `leader_trades`
- `live_orders`
- `live_trading_events`
- `market_data_events`
- `market_data_status`
- `markets`
- `order_book_snapshots`
- `paper_fills`
- `paper_orders`
- `paper_positions`
- `pinned_market_assets`
- `polymarket_http_logs`
- `polymarket_onchain_logs`
- `polymarket_onchain_fills`
- `polymarket_onchain_wallet_fills`
- `polymarket_onchain_wallet_executions`
- `polymarket_onchain_token_metadata`
- `polymarket_onchain_wallet_activity`
- `polymarket_onchain_wallet_activity_refresh_queue`
- `polymarket_onchain_wallet_positions`
- `polymarket_onchain_position_refresh_queue`
- `polymarket_onchain_wallet_performance`
- `polymarket_onchain_wallet_performance_refresh_queue`
- `polymarket_onchain_ingest_cursors`
- `risk_events`
- `scanner_status`
- `service_command_audit`
- `service_heartbeats`
- `signal_rejections`
- `signals`
- `trader_rules`
- `trader_leaderboard_snapshots`
- `trader_discovery_candidates`
- `traders`

## Project Layout

Solution: `PolyCopyTrader.sln`

Projects:

- `src/PolyCopyTrader.Domain`
  - core records/models;
  - configuration objects;
  - options validation;
  - storage connection resolver;
  - analytics math;
  - CSV formatting;
  - secret redaction helpers.
- `src/PolyCopyTrader.Storage`
  - PostgreSQL schema SQL;
  - schema initializer;
  - Npgsql connection factory;
  - repository implementation;
  - no-op repository fallback for dashboard/tests when storage is not configured.
- `src/PolyCopyTrader.Polymarket`
  - public Data API client;
  - public CLOB API client;
  - geoblock client;
  - Polymarket certificate pinning helper;
  - WebSocket market-data parser;
  - auth secret-provider abstraction;
  - L2 HMAC signing and header construction;
  - CLOB V2 order amount conversion;
  - CLOB V2 EIP-712 order building/signing;
  - dry-run and live trading HTTP client.
- `src/PolyCopyTrader.Strategy`
  - signal engine;
  - risk engine;
  - maker price calculator;
  - paper trading engine.
- `src/PolyCopyTrader.Service`
  - worker host;
  - startup safety check;
  - watchlist scanner;
  - signal processor;
  - paper trading processor;
  - live trading processor;
  - public market WebSocket background service;
  - local IPC HTTP server;
  - daily analytics worker;
  - manual trader discovery processor;
  - repository-backed API error sink.
- `src/PolyCopyTrader.Dashboard`
  - WPF dashboard;
  - MVVM main view model;
  - dashboard data service;
  - local IPC control client;
  - CSV export helper;
  - dashboard now reads environment variables as well as `appsettings.json`.
- `tests/PolyCopyTrader.Tests`
  - unit/integration tests for config, storage, public clients, scanner, signals,
    risk, paper trading, WebSocket parser, analytics, resilience, service controls,
    auth, dry-run signing, and live gating.

## Modes

Supported bot modes:

- `ReadOnly`: observe and persist read-only data; no paper order creation.
- `Paper`: create and maintain simulated paper orders and positions.
- `DryRun`: build/sign CLOB V2 payloads without submitting orders.
- `Live`: only usable if every explicit live safety gate passes.

Default config is read-only/paper safe. Live is disabled.

## Public API And Market Data

Public Polymarket support includes:

- Data API trader leaderboard;
- Data API user trades;
- Data API current positions;
- CLOB public order book;
- CLOB server time;
- CLOB midpoint/spread;
- Polymarket geoblock endpoint;
- public market WebSocket parser/client.

Outbound Polymarket connections support optional endpoint-host certificate pinning
through `Polymarket:CertificatePins`. Pins are SPKI SHA-256 values in
`sha256/<base64>` format. This is not an accept-any TLS bypass: if a host has pins,
the presented certificate key must match one of them and the certificate validity
window must be current.

The scanner stores leader trades and positions. It uses `takerOnly=false` where needed
so maker fills are not silently excluded. Invalid placeholder wallets are skipped and
recorded without crashing the service.

The WebSocket service subscribes only to relevant asset ids:

- open paper orders;
- open paper positions;
- recent accepted/high-score signals;
- dashboard/config pinned assets.

It reconnects with backoff, sends PING heartbeats, refreshes subscriptions, persists
market data status/events, and updates paper fills/marks from fresh top-of-book data.

## Trader Discovery

Trader discovery uses the public Polymarket Data API leaderboard.

When the operator clicks the dashboard `Find traders` button and
`TraderDiscovery:Enabled=true`, the service:

- fetches `LeaderboardPages` pages from `/v1/leaderboard`;
- uses `orderBy=PNL` to collect the successful leaderboard window;
- uses `orderBy=VOL` to collect high-volume traders where real negative PnL can
  be found;
- stores current merged leaderboard rows in `trader_leaderboard_snapshots`;
  this table has one row per `category + time_period + wallet`, with separate
  columns for the `orderBy=PNL` and `orderBy=VOL` leaderboard appearances;
- repeated `Find traders` runs update existing `trader_leaderboard_snapshots` rows
  instead of adding one row per run;
- selects the best `CandidatesPerSide` from the PnL window;
- selects the worst negative-PnL `CandidatesPerSide` from the volume window;
- fetches all-time leaderboard PnL/volume for each selected wallet using
  `/v1/leaderboard?timePeriod=ALL&user=<wallet>`;
- fetches recent trades for each selected wallet;
- fetches current positions for each selected wallet;
- stores enriched rows in `trader_discovery_candidates`;
- shows them in the dashboard Trader Discovery tab.

Important table distinction:

- `trader_leaderboard_snapshots` is the broad current candidate pool. It can contain
  hundreds or thousands of wallets because it stores all fetched PNL/VOL leaderboard
  rows merged by wallet.
- `trader_discovery_candidates` is the enriched shortlist. With
  `CandidatesPerSide=10`, it normally has 20 rows: 10 `BestPnl` and 10 `WorstPnl`.

There is intentionally no hosted background trader-discovery worker. Discovery should
download public leaderboard/trade/position data only after an explicit operator action.

This is candidate research only. Do not add a wallet to live copy behavior based on
leaderboard PnL alone. Review sample size, volume, categories, liquidity, recent trade
repeatability, and paper results first.

## Signal And Risk Behavior

The system is not blind copy-trading. A leader trade is only a candidate signal.

Signal filtering covers:

- supported side;
- stale trade age;
- leader trade size;
- category allowlist when category is known;
- available order book;
- spread thresholds;
- maker-price safety;
- slippage threshold;
- event-close window;
- liquidity/depth signals.

Risk controls cover:

- trade bankroll percent;
- market bankroll percent;
- trader bankroll percent;
- category bankroll percent;
- total deployed percent;
- daily loss percent;
- max open orders;
- order age/TTL.

Every rejection gets a reason code and is persisted.

## Paper Trading

Paper trading creates maker-style simulated orders from accepted signals.

Important assumptions:

- fills are approximate;
- a paper BUY fills only when observed best ask is at or below paper buy price;
- open long positions are marked using bid-side data, not midpoint or ask;
- stale WebSocket snapshots are ignored;
- REST order book data is a fallback;
- paper PnL is decision support, not broker-grade accounting.

Review paper results with `docs/paper_trading_evaluation.md` before considering any
dry-run/live work.

## Auth And Dry-Run Signing

Auth support was implemented in phases:

- research notes in `docs/auth_signing_plan.md`;
- L2 HMAC signer and header factory;
- secret-provider abstraction with environment variable and Windows Credential Manager
  providers;
- auth readiness service;
- CLOB V2 fixed-decimal amount conversion;
- CLOB V2 order builder;
- CLOB V2 EIP-712 signer;
- redacted dry-run payload persistence;
- dashboard visibility for dry-run orders.

Dry-run mode can sign locally if:

- `PolymarketAuth:DryRunSigningEnabled=true`;
- `PolymarketAuth:DryRunPrivateKeyName` resolves through the secret provider;
- the configured signing address matches the private key.

Dry-run mode must not submit orders.

## Live Trading Plumbing

Task 16 added live order support, but it is intentionally narrow.

Implemented live operations:

- signed CLOB V2 `POST /order`;
- cancel one order;
- cancel all orders;
- get/poll live order status;
- persist live orders;
- persist live trading events;
- maintenance loop for open orders;
- stale/expired order cancellation;
- kill switch integration;
- dashboard live order/event tabs;
- local IPC controls for live pause/resume/kill/cancel-all.

Initial live behavior:

- BUY only;
- GTD only;
- post-only/maker-only only;
- tiny max notional;
- local order book refetch before placement;
- risk and signal checks rerun before placement;
- signatures and secrets redacted from stored payloads/events.

No real live credentials were configured in the repository.

## Local IPC

The service exposes loopback-only HTTP IPC by default:

```text
http://127.0.0.1:5118/
```

Endpoints:

- `GET /health`
- `GET /status`
- `POST /pause`
- `POST /resume`
- `POST /pause-scanning`
- `POST /resume-scanning`
- `POST /pause-paper`
- `POST /resume-paper`
- `POST /pause-live`
- `POST /resume-live`
- `POST /kill-switch`
- `POST /clear-kill-switch`
- `POST /cancel-all-live`
- `POST /pin-asset?assetId=...`
- `POST /unpin-asset?assetId=...`

The dashboard calls these endpoints for controls. Commands are recorded in
`service_command_audit`.

## Dashboard

The WPF dashboard includes:

- Overview;
- Watchlist;
- Leader Trades;
- Signals;
- Dry Run Orders;
- Live Orders;
- Live Events;
- Paper Orders;
- Paper Positions;
- Market Data;
- Analytics;
- Risk;
- Diagnostics;
- Runbook;
- Logs;
- Dashboard Errors;
- Controls.
- Trader Discovery;
- Onchain Leaders;
- Onchain Rankings;
- Onchain Positions;
- Onchain Executions.

Dashboard storage behavior:

- if PostgreSQL is configured, it uses `PostgresAppRepository`;
- when PostgreSQL is configured, it initializes the PostgreSQL schema before the
  first read so newly added dashboard tables exist even if only the dashboard was
  restarted;
- if storage is missing, it uses `NoOpAppRepository` and shows empty/diagnostic state;
- after commit `abff28c`, dashboard supports env vars, including
  `POLYCOPYTRADER_POSTGRES_CONNECTION`.
- after on-chain ingestion work, dashboard has `Onchain Leaders`,
  `Onchain Rankings`, `Onchain Positions`, and `Onchain Executions` tabs fed by
  normalized Polygon `OrderFilled` wallet executions plus materialized activity,
  positions, and performance tables.
- after local error-history work, dashboard has `Dashboard Errors`, an in-memory
  tab that keeps the latest refresh, IPC command, and CSV export errors visible
  instead of only showing transient footer text. Rows auto-size for wrapped
  message/details text and the selected error can be copied to the clipboard.

Service storage behavior:

- service requires PostgreSQL and does not run with `NoOpAppRepository`;
- this is intentional so Polymarket HTTP logs, API errors, commands, and trading
  events are always persisted.

## Deployment And Operations

Deployment scripts live in `deploy/`:

- `install-service.ps1`
- `uninstall-service.ps1`
- `start-service.ps1`
- `stop-service.ps1`
- `backup-db.ps1`
- `README.md`

Operations docs live in `docs/`:

- `docs/runbook.md`
- `docs/incident_response.md`
- `docs/live_trading_checklist.md`
- `docs/paper_trading_evaluation.md`
- `docs/configuration_reference.md`
- `docs/auth_signing_plan.md`

The service has startup geoblock safety:

- runs Polymarket geoblock check from the actual host;
- writes a `StartupGeoblockCheck` live event;
- pauses live trading if blocked or if check fails.

## QA Commands

Standard checks:

```powershell
dotnet build
dotnet test
.\scripts\qa-check.ps1
```

Use this if another service instance already owns local IPC port `5118`:

```powershell
.\scripts\qa-check.ps1 -SkipRuntimeSmoke
```

Local service print config:

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -- --print-config
```

## Chronology And Commits

Implemented history:

- `394b17e` Scaffold PolyCopyTrader solution
- `dadff6d` Add configuration storage and logging
- `fad1f99` Switch storage from SQLite to PostgreSQL
- `bd05134` Add Polymarket public API clients
- `fe261ae` Add watchlist scanner
- `ac512d5` Add signal and risk engines
- `dac37c2` Add paper trading engine
- `431afe8` Add WPF monitoring dashboard
- `ef46869` Add worker service IPC controls
- `72990da` Add market data WebSocket monitoring
- `0a8302c` Add analytics reports and CSV export
- `85e9e26` Harden paper trading QA gate
- `aa0911f` Add Polymarket auth signing research
- `7b1681e` Add CLOB V2 auth HMAC infrastructure
- `c3efe7b` Add CLOB V2 dry-run order signing
- `b1fcc83` Add gated maker-only live trading
- `cc715c3` Add Windows VPS deployment scripts
- `f273b1b` Add operations runbooks
- `734210a` Add local PostgreSQL debug setup
- `abff28c` Support existing local PostgreSQL debugging
- `cdffa6c` Add Codex project memory
- `91bea5f` Add Polymarket trader discovery
- `fe2e5e1` Make trader discovery manually triggered
- `e26b1eb` Add Polymarket certificate pinning
- `6adaeb0` Add Polymarket pin export helper
- `fc9a8a3` Add Polymarket HTTP request logging
- `716ec7e` Preserve query string in Polymarket HTTP logs
- `41cf198` Require database in service debug profile
- `437d089` Require service PostgreSQL storage
- `55570a2` Add all-time trader discovery metrics
- `d25d4eb` Deepen trader discovery search
- `8519b5e` Merge trader leaderboard snapshots by wallet

## Known Limitations

Known at the time of this note:

- auth support does not create or derive Polymarket API keys;
- user-authenticated WebSocket channel is not implemented;
- trader enable/disable and cancel selected paper order dashboard buttons are still
  placeholders unless later tasks add command-specific IPC;
- strategy has not yet been validated over a meaningful real paper-trading sample;
- current watchlist in default config contains a disabled placeholder wallet;
- local database uses the `postgres` superuser for convenience; for production/VPS use
  a dedicated least-privilege database user;
- Docker local PostgreSQL support exists, but the user's preferred local debugging path
  is their installed Windows PostgreSQL on port 5432;
- no production VPS deployment has been completed yet;
- no live Polymarket credentials or private keys have been configured in repo.
- Dashboard Trader Discovery currently displays the enriched shortlist from
  `trader_discovery_candidates`, not the full merged `trader_leaderboard_snapshots`
  pool.
- Dashboard Onchain Rankings is an activity ranking only and now reads the
  materialized wallet activity table to avoid refresh-time aggregation over
  millions of wallet execution rows. Onchain Positions reads the materialized
  positions table and exposes resolved PnL when Gamma metadata has a winning
  outcome. Onchain Leaders adds a first heuristic profitability score over those
  positions, but mark-to-market and score tuning are still future work; sample
  quality matters.

## Recommended Next Work

Recommended local sequence:

1. Make sure Visual Studio or the shell sees `POLYCOPYTRADER_POSTGRES_CONNECTION`.
2. Run `.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase`.
3. Run `.\scripts\run-local-dashboard.ps1 -NoPostgres` in a second terminal.
4. Click `Find traders` to refresh deep discovery.
5. Inspect `trader_leaderboard_snapshots`, `trader_discovery_candidates`, and
   `polymarket_http_logs`.
6. Add real watchlist trader wallet(s) through configuration, still with live disabled.
7. Let paper mode collect data.
8. Review `docs/paper_trading_evaluation.md`.
9. Only after good paper evidence, test `DryRun` signing with a non-funded test key or
   proper secret provider setup.
10. Before any live work, re-check official Polymarket docs and run the live checklist.

Recommended deployment sequence:

1. Prepare Windows VPS.
2. Install PostgreSQL or configure managed PostgreSQL.
3. Configure secrets through env vars or Windows Credential Manager.
4. Run `.\scripts\qa-check.ps1` on the VPS.
5. Install service using `deploy/install-service.ps1`.
6. Run in `ReadOnly` or `Paper` first.
7. Verify startup geoblock from the VPS.
8. Confirm backups with `deploy/backup-db.ps1`.
9. Keep live trading off until paper and dry-run evidence are reviewed.

## Official Docs To Re-Check Before Auth/Live Changes

These links were used during implementation, but API details can change:

- Polymarket CLOB V2 migration: `https://docs.polymarket.com/v2-migration`
- Polymarket authentication: `https://docs.polymarket.com/api-reference/authentication`
- Polymarket post order: `https://docs.polymarket.com/api-reference/trade/post-a-new-order`
- Polymarket cancel all orders: `https://docs.polymarket.com/api-reference/trade/cancel-all-orders`
- Polymarket geoblock endpoint: `https://docs.polymarket.com/api-reference/geoblock`
- Polymarket leaderboard endpoint: `https://docs.polymarket.com/api-reference/core/get-trader-leaderboard-rankings`
- Polymarket rate limits: `https://docs.polymarket.com/api-reference/rate-limits`
- Polymarket WebSocket overview: `https://docs.polymarket.com/market-data/websocket/overview`

Future Codex must browse official docs again before changing authenticated or live
trading behavior.

# PolyCopyTrader Project Memory

Last updated: 2026-04-29, Europe/Sofia.

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
- local Windows PostgreSQL database `polycopytrader` created and initialized.
- trader discovery dashboard button and tab for best/worst PnL candidates.
- Polymarket certificate pinning for HTTP clients and the market WebSocket.
- Polymarket HTTP request/response audit table.

Latest verified code state on 2026-04-29:

- branch: `master`;
- latest commit at the time of this note: `abff28c Support existing local PostgreSQL debugging`;
- working tree was clean after that commit;
- `.\scripts\qa-check.ps1` passed;
- build had 0 warnings and 0 errors;
- tests passed: 91/91;
- local service smoke passed in `Paper` mode against PostgreSQL;
- live trading remained disabled.

After this note was originally created, trader discovery was added:

- config section: `TraderDiscovery`;
- table: `trader_discovery_candidates`;
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
  trader discovery and Polymarket HTTP logging changes, the schema initializer defines
  26 tables.

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
- `risk_events`
- `scanner_status`
- `service_command_audit`
- `service_heartbeats`
- `signal_rejections`
- `signals`
- `trader_rules`
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
  - no-op repository fallback when storage is not configured.
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
- uses `orderBy=PNL`;
- selects the best `CandidatesPerSide` by PnL;
- selects the worst `CandidatesPerSide` by PnL within the fetched API window;
- fetches recent trades for each selected wallet;
- fetches current positions for each selected wallet;
- stores enriched rows in `trader_discovery_candidates`;
- shows them in the dashboard Trader Discovery tab.

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
- Controls.

Dashboard storage behavior:

- if PostgreSQL is configured, it uses `PostgresAppRepository`;
- if storage is missing, it uses `NoOpAppRepository` and shows empty/diagnostic state;
- after commit `abff28c`, dashboard supports env vars, including
  `POLYCOPYTRADER_POSTGRES_CONNECTION`.

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
- next commit after this memory update adds trader discovery for leaderboard best/worst
  candidates.
- later commit adds Polymarket certificate pinning for HTTP and WebSocket endpoints.
- later commit adds `scripts/get-polymarket-certificate-pins.ps1`.
- next commit adds `polymarket_http_logs` request/response auditing.

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

## Recommended Next Work

Recommended local sequence:

1. Set `POLYCOPYTRADER_POSTGRES_CONNECTION` in the shell, not in files.
2. Run `.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase`.
3. Run `.\scripts\run-local-dashboard.ps1 -NoPostgres` in a second terminal.
4. Add real watchlist trader wallet(s) through configuration, still with live disabled.
5. Let paper mode collect data.
6. Review `docs/paper_trading_evaluation.md`.
7. Only after good paper evidence, test `DryRun` signing with a non-funded test key or
   proper secret provider setup.
8. Before any live work, re-check official Polymarket docs and run the live checklist.

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
- Polymarket WebSocket overview: `https://docs.polymarket.com/market-data/websocket/overview`

Future Codex must browse official docs again before changing authenticated or live
trading behavior.

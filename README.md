# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 18 plus local debugging and trader discovery support. It contains project structure, typed configuration, PostgreSQL schema initialization, a basic repository, read-only Polymarket Data/CLOB/Geo clients, a Worker Service scanner/signal/paper/live loop, local dashboard controls, public market WebSocket monitoring, trader discovery, analytics reports, CSV export, diagnostics, a monitoring dashboard, L2 HMAC header infrastructure, dry-run CLOB V2 signing, manually gated tiny maker-only live order placement, Windows VPS deployment scripts, and operations runbooks.

## Safety

- Live trading exists only behind `Bot:Mode=Live`, `Bot:EnableLiveTrading=true`, `LiveTrading:ManualEnableCode=LIVE_TRADING_ENABLED`, auth readiness, geoblock, clock-drift, API-error, risk, order-book, and kill-switch gates.
- Implemented live trading is BUY-only, GTD-only, post-only/maker-only, tiny-size, and disabled by default.
- Private-key handling is limited to secret-provider lookup for dry-run/live signing. Keys are not requested, stored in appsettings, or logged.
- Auth supports secret lookup, L2 HMAC signatures, L2 headers, dry-run CLOB V2 order signing, live order signing/submission, cancellation, and readiness reporting.
- Live order payloads, responses, cancellations, and live trading events are persisted with secrets and signatures redacted.
- Default mode is read-only/paper-first by project policy.

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
POST /pin-asset?assetId=...
POST /unpin-asset?assetId=...
```

Dashboard pause/resume, kill-switch, paper-control, and asset pin/unpin buttons call these endpoints. Commands are recorded in `service_command_audit`.

## Windows Service

Publish the service and install it with Windows Service Control Manager:

```powershell
dotnet publish src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -c Release -o .\publish\service
sc.exe create PolyCopyTrader.Service binPath= "$PWD\publish\service\PolyCopyTrader.Service.exe" start= delayed-auto
sc.exe start PolyCopyTrader.Service
```

Use `sc.exe stop PolyCopyTrader.Service` and `sc.exe delete PolyCopyTrader.Service` to stop/remove it. Keep `POLYCOPYTRADER_POSTGRES_CONNECTION` configured as a machine/user environment variable for the service account.

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

The dashboard polls PostgreSQL every `Dashboard:RefreshIntervalSeconds`. It shows overview metrics, watchlist/scanner status, leader trades, signals and rejection reasons, dry-run orders, paper orders, paper positions, market data, analytics reports, risk usage, and API/risk logs. If PostgreSQL is not configured, it opens with empty states and a clear storage status.

## Storage

The service uses PostgreSQL through Npgsql. Do not store credentials in repository files. Configure the connection string through the `POLYCOPYTRADER_POSTGRES_CONNECTION` environment variable.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

`PolyCopyTrader.Service` requires PostgreSQL storage. If no PostgreSQL connection string is configured, the service fails on startup instead of silently using a no-op repository. This keeps Polymarket HTTP logs, API errors, commands, and trading events from disappearing during debugging. The dashboard can still open without storage and will show empty/diagnostic states.

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

- Data API: trader leaderboard, user trades, and current positions.
- CLOB public API: order book, server time, midpoint, and spread.
- Geo endpoint: current geoblock status.

User trade calls explicitly send `takerOnly=false` when requested so maker fills are not silently excluded. HTTP failures are retried for transient `429`/`5xx` responses and persisted to `ApiErrors` through PostgreSQL.

Every Polymarket HTTP attempt is also written to PostgreSQL table `polymarket_http_logs` when storage is configured. Rows include component, operation, method, request URL, request/response UTC timestamps, duration, attempt number, HTTP status, success flag, response body preview, and error message. Request bodies and auth headers are not stored.

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

## Auth Research

Task 13 added research notes in `docs/auth_signing_plan.md`. Task 14 added native C# L2 HMAC signing, L2 header construction, secret-provider abstraction, and auth readiness reporting under `src/PolyCopyTrader.Polymarket/Auth`. Task 15 added native C# CLOB V2 order amount conversion, order construction, EIP-712 dry-run signing, redacted payload rendering, and dashboard/storage visibility for dry-run orders. Task 16 added gated live `POST /order`, cancel-one, cancel-all, and order-status polling support.

No API credentials are created or derived. `PolymarketAuth` config contains provider and lookup names only; secret values must live in environment variables or Windows Credential Manager. Dry-run signing may load a private key only through `DryRunPrivateKeyName`; live signing uses `OrderSigningPrivateKeyName`. Missing or mismatched keys fail closed. Test signing uses a deterministic public development key that must never be funded.

## Market WebSocket

When `Bot:UseWebSockets` and `MarketDataWebSocket:Enabled` are true, the service runs a public market WebSocket client against `wss://ws-subscriptions-clob.polymarket.com/ws/market`.

The subscription set is intentionally narrow. The service subscribes only to:

- open paper-order asset ids;
- open paper-position asset ids;
- recent accepted/high-score signal asset ids;
- asset ids pinned through config or dashboard IPC.

The WebSocket client sends `PING` heartbeats, reconnects with backoff, resubscribes after reconnect, refreshes subscriptions dynamically, persists connection status to `market_data_status`, persists received events to `market_data_events`, and writes top-of-book snapshots to `order_book_snapshots`.

## Watchlist Scanner

The service scans enabled `Watchlist:Traders` entries on `Bot:PollIntervalSeconds`. Each enabled wallet is validated before any API call. Recent trades are fetched with `takerOnly=false`, deduplicated, persisted to `LeaderTrades`, and queued as in-memory candidates for the future signal engine. Current positions are written as snapshots to `LeaderPositions`.

Scanner health is persisted to `scanner_status` with last success/error timestamps and per-loop fetched/stored counts. Invalid placeholder wallets are warned and skipped without crashing the service.

## Trader Discovery

Trader discovery is operator-triggered from the dashboard. When `TraderDiscovery:Enabled=true`, the dashboard `Find traders` button asks the service to fetch the full configured Polymarket leaderboard window twice: `orderBy=PNL` for successful traders and `orderBy=VOL` for high-volume loss candidates. Raw leaderboard rows are stored in `trader_leaderboard_snapshots`; the best PnL candidates and the worst negative-PnL volume candidates are enriched with all-time leaderboard PnL/volume for the same wallet, recent trades, and current positions, then stored in `trader_discovery_candidates`.

Run the service and click `Find traders` in the dashboard controls:

```powershell
.\scripts\run-local-service.ps1 -Mode Paper -NoPostgres -RequireDatabase
```

The dashboard shows refreshed shortlist rows in the Trader Discovery tab. The `PnL`/`Volume` columns are for the configured discovery period, while `All PnL`/`All Volume` are all-time sanity-check metrics fetched by wallet. Use this only for candidate research; a high leaderboard PnL is not enough to add a wallet to the watchlist without paper evaluation.

## Signal And Risk Engines

Queued leader trades are evaluated by `DefaultSignalEngine` after the scanner stores them. The engine rejects unsupported sides, stale trades, small leader trades, missing/wide order books, unsafe maker prices, excessive slippage, category mismatches when category is known, and markets too close to event end. Accepted decisions produce proposed paper-order details only; no order placement happens in this task.

`DefaultRiskEngine` enforces configured bankroll limits for trade, market, trader, category, total deployed exposure, daily loss, and max open orders. Rejected decisions are persisted as `SignalRejection` reason codes.

## Paper Trading

In `Paper` mode, accepted signals create `PaperOrder` records with the proposed maker price, size, notional, and configured TTL. `PaperTradingProcessor` expires stale pending orders and simulates conservative approximate fills from fresh WebSocket order books first, falling back to observed REST CLOB order books.

For paper BUY orders, a fill is only simulated when `bestAsk <= paperBuyPrice`. Fills are stored as `PaperFill` records with `SimulatedApproximate` evidence. Long positions are updated with weighted-average cost and valued using the current bid, not midpoint or ask.

WebSocket market-data updates also dispatch into paper trading so pending orders can fill and paper positions can be re-marked without waiting for the next scanner loop. Stale WebSocket snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.

## Dry Run Trading

In `DryRun` mode, accepted signals produce CLOB V2 order payloads without sending them to Polymarket. The dry-run path validates tick size, minimum size, price, signature type, signer/funder addresses, order type, and GTD expiration. BUY and SELL amounts are converted with 6-decimal fixed math according to the official V2 order model.

When `PolymarketAuth:DryRunSigningEnabled` is true and `DryRunPrivateKeyName` resolves through the configured secret provider, the app signs the order locally with the V2 EIP-712 domain. If the key is absent, the signer address does not match, or validation fails, the result is stored as `DryRunUnsigned` or `DryRunRejected`. Stored payloads are redacted and no `POST /order`, cancel, or authenticated trading HTTP call is made.

## Live Trading

Live trading is disabled by default. To place any live order, all gates must pass: `Bot:Mode` must be `Live`, `Bot:EnableLiveTrading` must be `true`, `LiveTrading:ManualEnableCode` must equal `LIVE_TRADING_ENABLED`, auth must be configured, geoblock must be clear from the machine running the service, CLOB server time must be within drift limits, no API-error or daily-loss lockout may be active, and the local kill switch/live pause must be clear.

Initial live orders are BUY-only, GTD-only, post-only, and capped by `LiveTrading:MaxOrderNotionalUsd` plus live bankroll percentages. Before placement the service refetches the order book, reruns signal/risk evaluation, verifies the maker price does not cross the best ask, blocks crypto/sports text matches, signs the CLOB V2 payload locally, and sends `POST /order` with L2 headers. Live orders and live events are stored in PostgreSQL; the maintenance loop polls order status and cancels expired/stale orders. The kill switch pauses new live orders and requests cancel-all.

On startup the service checks Polymarket geoblock status from the actual host and writes a `StartupGeoblockCheck` live event. If blocked or if the check fails, live trading is paused.

## Analytics And Reporting

The service automatically generates daily reports into `daily_reports` when `Analytics:DailyReportGenerationEnabled` is true. Reports are recalculated every `Analytics:DailyReportRefreshMinutes` for the current UTC day and the previous UTC day.

Dashboard analytics include:

- daily summary: signals observed/accepted/rejected, paper orders, fills, expired orders, paper PnL, open paper exposure, top rejection reasons, API errors;
- trader performance: signal counts, acceptance rate, fill rate, average lag, leader/proposed price comparison, approximate paper PnL, rejection reasons;
- category performance: grouped by `markets.category`, or `unknown` when category is not available;
- execution quality: leader price, proposed price, fill price, price deltas, lag/spread, and bid/ask/mid snapshots after 1m, 5m, and 30m when stored market data exists;
- rejection analysis: reason code counts and share of rejected signals.

CSV export from the dashboard writes `LeaderTrades.csv`, `Signals.csv`, `SignalRejections.csv`, `PaperOrders.csv`, `PaperPositions.csv`, and `DailyReports.csv` under `Analytics:CsvExportDirectory`.

Interpret paper results conservatively. Paper fills are approximate, long positions are marked from bid-side data, and historical daily PnL is a generated snapshot over stored paper positions rather than broker-grade accounting. Use the reports to compare filters, traders, categories, and execution quality before considering any live-trading work.

## Dashboard Screens

- Overview: service heartbeat, mode, storage/API status, scanner status, bankroll, exposure, PnL.
- Watchlist: configured traders plus scanner counters and errors.
- Trader Discovery: leaderboard best/worst PnL candidates enriched with recent trades and positions.
- Leader Trades: latest observed leader trades.
- Signals: accepted/rejected decisions, reason codes, proposed paper details.
- Dry Run Orders: unsigned/signed/rejected dry-run payload records and validation messages.
- Live Orders: submitted/live/rejected/cancelled live order records.
- Live Events: live placement, cancellation, polling, and error audit entries.
- Paper Orders: lifecycle, TTL, fill timestamps, linked signal id.
- Paper Positions: size, average price, estimated value, unrealized PnL.
- Market Data: latest WebSocket/market-data asset snapshots, bid, ask, spread, update time.
- Analytics: daily, trader, category, execution-quality, and rejection reports.
- Risk: configured limits and current usage.
- Diagnostics: sanitized config summary, storage status, auth status, service/scanner/WebSocket status, watchlist summary, latest API errors, and risk usage.
- Runbook: local paths and purposes for the operations documents.
- Logs: API errors, risk events, service commands, and market-data events.
- Controls: pause/resume scanner, pause/resume paper/live trading, kill switch, clear kill switch, cancel all live orders, and asset pin/unpin through localhost IPC.
- Trader discovery refresh is also a localhost IPC command and only runs when the operator presses the dashboard button.

## Troubleshooting

- PostgreSQL not configured: set `POLYCOPYTRADER_POSTGRES_CONNECTION` and restart the service. The service does not run with no-op storage.
- Invalid watchlist wallet: the scanner skips placeholder/invalid wallets, records a warning status, and keeps the service running.
- Polymarket TLS certificate errors: configure `Polymarket:CertificatePins` only after verifying the current endpoint certificate pin out of band. Do not use an accept-any certificate callback in production.
- HTTP 429/5xx from Polymarket: public clients retry transient failures according to `Polymarket:MaxRetries` and record API errors when retries are exhausted.
- Malformed API response: the failing operation is recorded as an API error; scanner/signal/paper loops continue on later cycles.
- WebSocket disconnected/stale: the market WebSocket reconnects with backoff and stale snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.
- IPC unavailable: check whether `http://127.0.0.1:5118/` is already in use, then run `GET /health` or the QA script runtime smoke.
- Database temporarily unavailable: loop-level error recording is best-effort and will not crash the worker if error persistence also fails.
- VPS backup failing: confirm PostgreSQL client tools are installed and `POLYCOPYTRADER_POSTGRES_CONNECTION` is available to the scheduled task or service account.

Do not enable live trading unless `dotnet build`, `dotnet test`, `--print-config`, runtime IPC smoke, geoblock check from the actual host, and cancel-all testing pass.

## Known Limitations

- Auth support does not create or derive API keys yet.
- Trader enable/disable and cancel selected paper order dashboard buttons are placeholders until command-specific IPC is added.
- User-authenticated WebSocket channel is not implemented yet.

## Next Recommended Task

All numbered Codex tasks in `Codex/00_INDEX.md` are implemented. Next work should be operational validation on the intended VPS with real PostgreSQL and secret-provider configuration.

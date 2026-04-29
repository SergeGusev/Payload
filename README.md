# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 12: testing, QA, and hardening. It contains project structure, typed configuration, PostgreSQL schema initialization, a basic repository, read-only Polymarket Data/CLOB/Geo clients, a Worker Service scanner/signal/paper loop, local dashboard controls, public market WebSocket monitoring, analytics reports, CSV export, diagnostics, and a read-only monitoring dashboard.

## Safety

- No live trading exists in this scaffold.
- No authenticated Polymarket endpoints exist.
- No private key handling exists.
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

Run the repeatable pre-auth QA gate before any authenticated/live-trading work:

```powershell
.\scripts\qa-check.ps1
```

Use `.\scripts\qa-check.ps1 -SkipRuntimeSmoke` when another service instance is already bound to the local IPC port.

## Run Service

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

The service runs the scanner, signal engine, paper engine, heartbeat writer, daily analytics report generator, market WebSocket client, and localhost-only IPC server. It writes rolling logs under its output `logs` directory.

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

## Print Config Summary

```powershell
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj -- --print-config
```

The summary is sanitized and does not include secrets. Live trading is disabled by configuration and validation.

## Run Dashboard

```powershell
dotnet run --project src/PolyCopyTrader.Dashboard/PolyCopyTrader.Dashboard.csproj
```

The dashboard is read-only and polls PostgreSQL every `Dashboard:RefreshIntervalSeconds`. It shows overview metrics, watchlist/scanner status, leader trades, signals and rejection reasons, paper orders, paper positions, market data, analytics reports, risk usage, and API/risk logs. If PostgreSQL is not configured, it opens with empty states and a clear storage status.

## Storage

The service uses PostgreSQL through Npgsql. Do not store credentials in repository files. Configure the connection string through the `POLYCOPYTRADER_POSTGRES_CONNECTION` environment variable.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

If no PostgreSQL connection string is configured, storage is disabled and the service uses a no-op repository. Set `Storage:RequireConfiguredDatabase` to `true` for production/VPS runs.

## Polymarket Public APIs

The `PolyCopyTrader.Polymarket` project contains read-only clients for:

- Data API: trader leaderboard, user trades, and current positions.
- CLOB public API: order book, server time, midpoint, and spread.
- Geo endpoint: current geoblock status.

User trade calls explicitly send `takerOnly=false` when requested so maker fills are not silently excluded. HTTP failures are retried for transient `429`/`5xx` responses and persisted to `ApiErrors` through the configured repository. When PostgreSQL is not configured, the no-op repository keeps local scaffold runs read-only and dependency-free.

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

## Signal And Risk Engines

Queued leader trades are evaluated by `DefaultSignalEngine` after the scanner stores them. The engine rejects unsupported sides, stale trades, small leader trades, missing/wide order books, unsafe maker prices, excessive slippage, category mismatches when category is known, and markets too close to event end. Accepted decisions produce proposed paper-order details only; no order placement happens in this task.

`DefaultRiskEngine` enforces configured bankroll limits for trade, market, trader, category, total deployed exposure, daily loss, and max open orders. Rejected decisions are persisted as `SignalRejection` reason codes.

## Paper Trading

In `Paper` mode, accepted signals create `PaperOrder` records with the proposed maker price, size, notional, and configured TTL. `PaperTradingProcessor` expires stale pending orders and simulates conservative approximate fills from fresh WebSocket order books first, falling back to observed REST CLOB order books.

For paper BUY orders, a fill is only simulated when `bestAsk <= paperBuyPrice`. Fills are stored as `PaperFill` records with `SimulatedApproximate` evidence. Long positions are updated with weighted-average cost and valued using the current bid, not midpoint or ask.

WebSocket market-data updates also dispatch into paper trading so pending orders can fill and paper positions can be re-marked without waiting for the next scanner loop. Stale WebSocket snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.

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
- Leader Trades: latest observed leader trades.
- Signals: accepted/rejected decisions, reason codes, proposed paper details.
- Paper Orders: lifecycle, TTL, fill timestamps, linked signal id.
- Paper Positions: size, average price, estimated value, unrealized PnL.
- Market Data: latest WebSocket/market-data asset snapshots, bid, ask, spread, update time.
- Analytics: daily, trader, category, execution-quality, and rejection reports.
- Risk: configured limits and current usage.
- Diagnostics: sanitized config summary, storage status, service/scanner/WebSocket status, watchlist summary, latest API errors, and risk usage.
- Logs: API errors, risk events, service commands, and market-data events.
- Controls: pause/resume scanner, pause/resume paper trading, kill switch, and asset pin/unpin through localhost IPC.

## Troubleshooting

- PostgreSQL not configured: set `POLYCOPYTRADER_POSTGRES_CONNECTION`, restart the service, and check the Diagnostics tab. In local scaffold runs, the no-op repository is expected when no connection string exists.
- Invalid watchlist wallet: the scanner skips placeholder/invalid wallets, records a warning status, and keeps the service running.
- HTTP 429/5xx from Polymarket: public clients retry transient failures according to `Polymarket:MaxRetries` and record API errors when retries are exhausted.
- Malformed API response: the failing operation is recorded as an API error; scanner/signal/paper loops continue on later cycles.
- WebSocket disconnected/stale: the market WebSocket reconnects with backoff and stale snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.
- IPC unavailable: check whether `http://127.0.0.1:5118/` is already in use, then run `GET /health` or the QA script runtime smoke.
- Database temporarily unavailable: loop-level error recording is best-effort and will not crash the worker if error persistence also fails.

Do not proceed to authenticated signing or live trading unless `dotnet build`, `dotnet test`, `--print-config`, and runtime IPC smoke pass.

## Known Limitations

- No auth/signing/live trading support.
- Trader enable/disable and cancel selected order dashboard buttons are placeholders until command-specific IPC is added.
- User-authenticated WebSocket channel is not implemented yet.

## Next Recommended Task

Implement `Codex/13_TASK_AUTH_SIGNING_RESEARCH_ONLY.md`.

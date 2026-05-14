# PolyCopyTrader

PolyCopyTrader is a Windows/.NET C# application for monitoring Polymarket traders and running a cautious copy-signal strategy.

This repository is currently at Task 18 plus local debugging, trader discovery, Gamma active-market ingestion, and paused on-chain discovery support. It contains project structure, typed configuration, PostgreSQL schema initialization, a basic repository, read-only Polymarket Data/CLOB/Gamma/Geo clients, a Worker Service scanner/signal/paper/live loop, local dashboard controls, public market WebSocket monitoring, trader discovery, Polygon `OrderFilled` ingestion with fresh catch-up for the live tail, analytics reports, CSV export, diagnostics, a monitoring dashboard, L2 API credential bootstrap, L2 HMAC header infrastructure, dry-run CLOB V2 signing, manually gated tiny maker-only live order placement, Windows VPS deployment scripts, and operations runbooks.

## Safety

- Live trading exists only behind `Bot:Mode=Live`, `Bot:EnableLiveTrading=true`, `LiveTrading:ManualEnableCode=LIVE_TRADING_ENABLED`, auth readiness, geoblock, clock-drift, API-error, risk, order-book, and kill-switch gates.
- Implemented live trading is BUY-only, tiny-size, and disabled by default. The legacy Follow Leader live path remains tightly gated; the BTC paper/live shadow test path is limited to explicitly allowed BTC variants, currently `BTC Up or Down 5m Skip 1` and `BTC Up or Down 5m More 150 Below 65`, and submits GTD limit BUY orders with `post_only=false`, a local market-relative cancel deadline, and a CLOB GTD expiration that includes the configured security buffer.
- Private-key handling is limited to secret-provider lookup for dry-run/live signing. Keys are not requested, stored in appsettings, or logged.
- Auth supports secret lookup, L2 HMAC signatures, L2 headers, dry-run CLOB V2 order signing, live order signing/submission, cancellation, and readiness reporting.
- Live order payloads, responses, cancellations, settlement accounting, and live trading events are persisted with secrets and signatures redacted.
- BTC paper/live shadow test decisions, correlation ids, linked Paper/Live orders, and discrepancy records are persisted so one real order can be compared against its Paper-shadow model.
- Paper trading can optionally keep running in `Live` mode with `PaperTrading:RunInLiveMode=true`; this is shadow Paper only and does not relax any live-order gate.
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

The dashboard polls PostgreSQL every `Dashboard:RefreshIntervalSeconds`, default `60`, and reads localhost IPC `/status` for the prominent service-state banner. The top database selector switches dashboard data between the configured local PostgreSQL connection and a remote PostgreSQL connection using the same connection string with host `192.168.0.1`. Strategy performance rows are cached separately and refreshed every `Dashboard:StrategyRefreshIntervalSeconds`, default `60`, unless a strategy command invalidates the cache. DataGrid row selection is restored across refreshes by stable row keys so horizontal inspection is not interrupted by the refresh cycle. It shows whether the service is running, paused, unavailable, or under kill switch, plus pause flags and the current service loop. It also shows overview metrics, watchlist/scanner status, cumulative strategy performance, short-window strategy performance for `1h` / `6h` / `24h`, decision-health entry delay metrics, on-chain rankings/fills, leader trades, signals and rejection reasons, dry-run orders, paper orders, paper positions, market data, analytics reports, risk usage, and API/risk logs. If PostgreSQL is not configured, it opens with empty states and a clear storage status. Schema initialization is owned by the service so dashboard startup is not blocked by database migrations or index creation.

## Storage

The service uses PostgreSQL through Npgsql. Do not store credentials in repository files. Configure the connection string through the `POLYCOPYTRADER_POSTGRES_CONNECTION` environment variable.

```powershell
$env:POLYCOPYTRADER_POSTGRES_CONNECTION="Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require"
dotnet run --project src/PolyCopyTrader.Service/PolyCopyTrader.Service.csproj
```

`PolyCopyTrader.Service` requires PostgreSQL storage. If no PostgreSQL connection string is configured, the service fails on startup instead of silently using a no-op repository. This keeps Polymarket HTTP logs, API errors, commands, and trading events from disappearing during debugging. The dashboard can still open without storage and will show empty/diagnostic states.

Paper/Live shadow testing stores the shared BTC decision in `paper_live_shadow_decisions`, links `paper_orders` and `live_orders` by `correlation_id`, and writes fatal mismatches to `paper_live_shadow_discrepancies`.

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

### BTC 5m Statistics Strategy

`BTC Up or Down 5m Statistics` is a read-only research strategy. When
`BtcUpDown5mStatistics:Enabled=true`, the service polls the current Binance
BTC/USDT reference while active BTC 5-minute markets are open, looks up
`btc_5m_history` around the current `(seconds, cents)` point with four-point
interpolation, compares the estimated Up/Down probability with the current
Polymarket Up/Down quote, and writes one row per observation to
`btc_up_down_5m_statistics_ticks`. It records decisions such as
`insufficient_history`, `market_price_missing`, `no_positive_edge`,
`up_above_market`, and `down_above_market`; it never creates Paper, dry-run, or
live orders.

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

### BTC Reference Diagnostics

The service keeps the Binance BTC/USDT trade stream as the operational BTC
reference for Middle strategies. When `ChainlinkBtcUsdDiagnostics:Enabled=true`,
an additional diagnostic worker polls Chainlink's BTC/USD Data Streams live-data
endpoint every 10 seconds, pairs the nearest Chainlink benchmark with the latest
fresh Binance trade point, and stores the result in
`btc_usd_reference_correlation_samples`. These rows are for correlation analysis
only and do not influence strategy decisions.

When `BtcOrderBookLagDiagnostics:Enabled=true`, the service also stores a
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

Task 13 added research notes in `docs/auth_signing_plan.md`. Task 14 added native C# L2 HMAC signing, L2 header construction, secret-provider abstraction, and auth readiness reporting under `src/PolyCopyTrader.Polymarket/Auth`. A later safe bootstrap command added L1 `ClobAuth` signing for CLOB L2 API credential derive/create and Windows Credential Manager storage. Task 15 added native C# CLOB V2 order amount conversion, order construction, EIP-712 dry-run signing, redacted payload rendering, and dashboard/storage visibility for dry-run orders. Task 16 added gated live `POST /order`, cancel-one, cancel-all, and order-status polling support.

`PolymarketAuth` config contains provider and lookup names only; secret values must live in environment variables or Windows Credential Manager. To derive or create CLOB L2 API credentials without sending orders, run `.\PolyCopyTrader.Service.exe --bootstrap-polymarket-api-credentials` from the service output directory while Live is disabled. The command prints only redacted status and Credential Manager target names. Use `--auth-readiness-smoke` to validate local L2 HMAC/header construction without sending HTTP requests, `--clob-authenticated-read-smoke` to validate the same credentials against read-only CLOB `GET /trades`, and `--dry-run-signing-smoke` to validate local order EIP-712 signing. `--clob-cancel-all-smoke` calls CLOB `DELETE /cancel-all`; run it only after confirming any open account orders may be cancelled. Dry-run signing may load a private key only through `DryRunPrivateKeyName`; live signing uses `OrderSigningPrivateKeyName`. Missing or mismatched keys fail closed. Test signing uses a deterministic public development key that must never be funded.

## Market WebSocket

When `Bot:UseWebSockets` and `MarketDataWebSocket:Enabled` are true, the service runs a public market WebSocket client against `wss://ws-subscriptions-clob.polymarket.com/ws/market`.

The subscription set is controlled by `MarketDataWebSocket:SubscriptionScope`. `AllActiveMarkets` subscribes to all active Gamma markets discovered by `GammaMarketIngestion`. The current service config uses `BtcUpDown5mOnly`, which still upserts every active Gamma market to PostgreSQL but registers only BTC Up/Down 5m markets in the WebSocket subscription registry. For each registered subscription market, the service updates an in-memory `assetId -> market snapshot` cache before writing the page to PostgreSQL. The snapshot keeps the compact decision-relevant fields: market ids/slugs/title, event/category context, outcome mapping, active/closed/archived/restricted/order-book flags, liquidity/volume, best bid/ask, spread, last trade price, order minimum size, price tick size, and relevant timestamps. It intentionally does not keep the full Gamma raw JSON or long description in memory.

New token ids are subscribed through `assets_ids` as soon as the WebSocket supervisor observes them, and reconnects resubscribe to the current in-memory set. After a full Gamma scan reaches the empty page, token ids missing from the latest `active=true&closed=false` result are removed from the in-memory cache and unsubscribed. WebSocket `book`, `price_change`, `best_bid_ask`, and `last_trade_price` messages update cached bid/ask/last-trade fields on the fly; `price_change` deltas are applied to the last full `book` snapshot so known depth is not replaced by top-of-book-only updates. `market_resolved` removes the resolved asset from the active subscription cache as an early lifecycle hint.

The WebSocket client also keeps supporting operational subscriptions for:

- open paper-order asset ids;
- open paper-position asset ids;
- recent accepted/high-score signal asset ids;
- asset ids pinned through config or dashboard IPC.

`MarketDataWebSocket:MaxSubscribedAssets=0` means no local cap; prefer `SubscriptionScope` for semantic narrowing because a numeric cap can exclude the BTC assets the strategy needs. The WebSocket supervisor shards the desired asset ids across multiple `ClientWebSocket` connections instead of using one huge all-active subscription. `MarketDataWebSocket:ShardMaxAssets` defaults to `3000`, `MaxShardConnections` defaults to `64`, and all outcomes for the same market/condition are kept on the same shard. Shard assignment is stable while the Gamma full scan is still discovering pages: new token ids are sent to existing shard connections with dynamic subscribe messages when there is capacity, instead of restarting all existing shards on every page. Each shard sends `PING` heartbeats, reconnects with backoff, and resubscribes after reconnect. Subscription messages are sent in `MarketDataWebSocket:SubscriptionBatchSize` chunks to avoid huge single payloads.

The supervisor checks shards every `MarketDataWebSocket:WatchdogIntervalSeconds`, default `10`. A shard with a failed receive/heartbeat loop reconnects itself; a still-open shard that has not received any protocol frame for `WatchdogStaleSeconds`, default `90`, is reopened by the supervisor. The aggregate status is stored as `PolymarketMarketWebSocket` in `market_data_status`; individual rows are stored as `PolymarketMarketWebSocket:shard-001`, `PolymarketMarketWebSocket:shard-002`, and so on.

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

`DefaultRiskEngine` enforces configured bankroll limits for trade, market, trader, category, total deployed exposure, daily loss, and max open orders. Rejected decisions are persisted as `SignalRejection` reason codes.

## Strategies

The BTC strategy set also includes Paper-only capped Gamma comparison variants: `More 60 Gamma Below 70/80`, `More 90 Gamma Below 70`, `More 120 Gamma Below 65/70`, and `More 150 Gamma Below 70/80`. They keep the old Gamma-first `More` outcome selection, then place a GTD limit BUY at the configured cap; by default the local order deadline is one minute before market end. They are for comparison only and are not live-enabled.

Trading strategies are stored in PostgreSQL table `strategies`. Built-in rows include `Follow leader` (`follow_leader`, id `f0110a0d-1ead-4c00-8b01-000000000001`) and the experimental BTC 5-minute family: 9 `BTC Up or Down 5m Less {Secs}` rows, 9 `BTC Up or Down 5m More {Secs}` rows, 9 `BTC Up or Down 5m Less {Secs} Gamma` rows, and 9 `BTC Up or Down 5m More {Secs} Gamma` rows, with `{Secs}` from `30` through `270` in 30-second steps, plus `BTC Less 180 Martin`, capped `More` variants (`More 30 Below 55`, `More 60 Below 60/55`, `More 90 Below 70/65/60/55`, `More 120 Below 70`, `More 150 Below 65`, and `More 270 Below 65/60`), capped `Less` variants (`Less 60 Below 20`, `Less 90 Below 20`, and `Less 120 Below 20/30`), 5 `BTC Up or Down 5m Middle {N}` rows, threshold `BTC Up or Down 5m Middle {N} 0.1..0.9 bps` rows for each `N=1..5`, 5 `BTC Up or Down 5m Middle {N} Revert` rows, 5 `BTC Up or Down 5m Skip {N}` rows, 5 `BTC Up or Down 5m Skip {N} Revert` rows, fixed-direction `BTC Up or Down 5m Up` / `BTC Up or Down 5m Down` rows, `BTC Up or Down 5m Binance`, threshold `BTC Up or Down 5m Binance 0.1..0.9/1/2/5 bps`, fixed-price `BTC Up or Down 5m Binance 45/47/49`, delayed `BTC Up or Down 5m Binance 15s/30s/45s`, `BTC Up or Down 5m Binance Clever`, `BTC Up or Down 5m Binance Clever Aggressive/Conservative`, fair-value `BTC Up or Down 5m Binance Edge 2/4/6`, 17 fixed-price `BTC Up or Down 5m Prev Score Countertrend 10..90` rows, `BTC Up or Down 5m Ensemble 2 of 3`, `BTC Up or Down 5m Dynamic Markov`, and `BTC Up or Down 5m Strategy Selector`. It also includes 960 Paper-only fixed-direction pre-open variants across BTC `5m`, `15m`, `1h`, and `4h` markets: 640 `PreOpen Half` / `PreOpen Full` Always-Up and Always-Down BUY variants at every limit price from `0.49` down to `0.10`, plus 320 `PreOpen Full ... Sell` variants that add the final-quarter protective SELL exit. Paper, dry-run, and live order rows carry `strategy_id`, so strategy variants can run side by side and be compared by their order/fill outcomes. The dashboard `Strategies` tab aggregates one row per strategy from `strategies`, `paper_orders`, open positions, settlements, BTC strategy run lifecycle rows, and `live_orders`. Its default nested `All` tab shows selectable/copyable strategy names without internal codes, includes Paper open-position mark-to-market `MtM PnL` / `MtM ROI %` beside closed-only `Realized` and `Closed ROI %`, shows decision-health `Avg delay s` / `Max delay s` between planned `entry_due_at_utc` and actual `entered_at_utc`, and shows separate Live outcome metrics: Live orders, filled/open/settled counts, won/lost, settled cost basis, realized PnL, win/loss rate, average win/loss, profit factor, expectancy, ROI, and latest live order/settlement timestamps. The same `Strategies` tab also contains nested `24 hours`, `6 hours`, and `1 hour` tabs with recent orders, fills, expired/open orders, entered/skipped/settled runs, wins/losses, win rate, ROI, realized PnL, filled cost, average fill price, entry-delay health metrics, top skip reason, and last activity timestamps. The `All` tab lets the `Enabled` flag be changed at runtime and exposes editable per-strategy `Paper $`, `Live $`, and live-only `Live bal` values. It also shows closed-outcome quality metrics (`Avg win`, `Avg loss`, `Profit factor`, and `Expectancy`) beside `Win %` so high win-rate but negative-expectancy strategies are visible. The service refreshes enabled strategy ids from PostgreSQL through a short in-memory cache; disabled strategies stop creating new Follow leader signals or BTC entries without a restart. Existing Paper positions can still be settled, and copied leader exits can still be tracked so disabling a strategy does not strand already-open accounting.

The `BTC Up or Down 5m` paper worker watches BTC 5-minute Gamma markets from `polymarket_gamma_markets`, stores one lifecycle row per enabled market/variant in `strategy_market_paper_runs`, then creates paper-only entries for each enabled variant's configured Paper stake multiplier when it becomes due. With `BtcUpDown5mStrategy:PaperTakerPricingEnabled=true`, the standard non-`Gamma` variants use Gamma only for market/outcome/token mapping and settlement metadata. The worker prices both outcome assets from fresh CLOB/WebSocket order-book depth, falls back to REST CLOB `/book` when cached depth is missing or stale, computes executable ask-depth VWAP, then selects `Less` as the lower executable VWAP and `More` as the higher executable VWAP. If an outcome book exists but its ask side is empty, the worker still creates a resting GTD BUY limit using the Gamma reference plus the configured reference slippage cap so the order can fill if matching asks appear later. The capped `Less ... Below ...` and `More ... Below ...` variants use the same CLOB-first selector at their respective delays, but now place a GTD limit BUY at the configured `Below` cap instead of requiring immediate executable liquidity below that cap. The `Gamma`-suffixed comparison variants intentionally use the older Gamma-first selector: `Less Gamma` picks the lower Gamma `outcomePrices` entry and `More Gamma` picks the higher Gamma entry, then the selected asset uses the CLOB/WebSocket quote as the GTD limit seed when ask depth exists, or the same resting-limit empty-ask fallback when it does not. These variants are for Paper comparison of the old selector against the CLOB-first selector; they do not place live orders. The quote-selection step walks executable ask depth while refusing ask levels above the configured max allowed price. GTD BUY sizing then computes the minimum notional needed to submit the current market `min_order_size`, adds a `10%` safety buffer, multiplies by the configured stake multiplier, rounds the target notional up to the next whole USD, and rounds order size to CLOB-compatible precision. CLOB/WebSocket/REST execution data is trusted for BTC taker Paper; Gamma/CLOB difference is kept as diagnostics rather than a skip reason. If spread is too wide on available executable quotes, an order book is missing or stale, market minimum submitted size is not met, executable sides are tied, or a standard selected Paper entry price is not below `0.5` for `Less` or not above `0.5` for `More`, the run is skipped with an explicit reason. BTC taker skips that have quote/order-book context also store `strategy_market_paper_runs.skip_diagnostics_json`, including cache status, REST `/book` usage, quote age, top bids/asks, and executable-depth flags. `paper_orders.raw_decision_json` stores the selected entry quote snapshot, source, quote age, best bid/ask, spread, top depth, Gamma reference, max price, stake multiplier, minimum notional, raw target notional, stake notional rounding mode, rounded target notional, estimated VWAP when executable depth exists, reserved target notional, `order_execution_mode=GTD`, `limit_price`, and an `outcome_selection_source` of `clob_executable_vwap`, `clob_resting_limit`, or `gamma_outcome_price`; GTD variants store final order diagnostics with `order_execution_mode=GTD`, `opening_limit_price_mode`, `limit_price`, `gtd_expiration_mode`, local `cancel_deadline_utc`, and `clob_wire_gtd_expiration_utc`; capped variants use `strategy_entry_price_cap` with `limit_price` equal to the cap, standard quote-seeded variants use `selected_entry_quote_price`, and empty-ask fallback variants use `resting_limit_no_executable_ask_depth`. `BTC Less 180 Martin` uses the same 180-second CLOB-selected lower executable outcome, but only enters after `BTC Up or Down 5m Less 180` has three fresh settled losses in a row. Martin uses the same configured stake multiplier; the service config keeps `MartinStakeLevels=1` so it does not escalate stake size. The `Middle {N}` variants run immediately at market open: they read the latest Binance BTC/USDT trade-stream price and compare it plus the latest `N-1` cached one-minute reference samples against the cached arithmetic mean; all values above mean buy `Down`, all below mean buy `Up`, equality or mixed sides skip. The `Middle {N} 0.1..0.9 bps` variants keep the same direction logic but require every compared price to be at least the configured bps distance from the mean, otherwise they skip with `btc_reference_mean_deviation_below_threshold`. The `Middle {N} Revert` variants inspect the same reference stack but invert the final Middle decision, so above mean buys `Up` and below mean buys `Down`. The `Skip {N}` variants inspect exactly the immediately previous `N` BTC 5-minute windows with no gaps, but infer those previous results from the closed market's CLOB `Up` order book instead of waiting for Gamma settlement: `Up mid = (best_bid + best_ask) / 2`, `Up mid >= 0.5` means previous result `Up`, and `Up mid < 0.5` means previous result `Down`. If the close-book order book is unavailable or lacks a usable bid/ask, the run is skipped with `btc_previous_close_book_orderbook_unavailable`; `skip_diagnostics_json` records the expected previous window, market/token ids, lookup reasons, and any partial bid/ask evidence so the frequency can be audited later. If the contiguous inferred results are all `Up`, the strategy buys `Down`; if all are `Down`, it buys `Up`; otherwise it skips. The `Skip {N} Revert` variants inspect the same strict contiguous close-book result stack but invert the final Skip decision, so consecutive `Up` buys `Up` and consecutive `Down` buys `Down`. Decision JSON records `decision_source=clob_close_book_midpoint`, the close-book best bid/ask/midpoint, the inferred `Up` midpoint, and result lag from previous market close to inference time. `Middle`, `Middle Revert`, `Skip`, `Skip Revert`, `Ensemble 2 of 3`, `Dynamic Markov`, and `Strategy Selector` create pending Paper BUY orders as ordinary GTD limit orders with dynamic break-even pricing. By default, BTC opening-limit GTD orders use `OpeningLimitExpireBeforeMarketEndSeconds` (`60`) as the local deadline offset from market close; `OpeningLimitGtdTtlSeconds` (`120`) remains the fallback when a market end is unavailable or the market-relative offset is disabled with `0`. Each variant uses its own recent settled win-rate, subtracts `OpeningLimitBreakEvenMargin` (`0.10` by default), caps the resulting BUY limit at `OpeningLimitMaxPrice` (`0.50`), and floors it to the configured tick before placing an order; insufficient history or a non-positive computed price skips the run. `BTC Up or Down 5m Up` and `BTC Up or Down 5m Down` wait until the market is actually accepting orders with an order book, then place a GTD BUY at fixed price `0.45` on the corresponding outcome. Until a new `Middle Revert` or `Skip Revert` variant has enough of its own settled rows, it bootstraps the dynamic price from the paired base strategy history by treating base losses as estimated Revert wins. The order size targets the minimum passing order size plus the `10%` safety buffer times the configured multiplier, rounded up to the next whole USD, with `post_only=false` diagnostics, not immediate fills and not live orders. The Paper open-order pipeline applies a balanced fill model while the GTD order is alive: each fill consumes visible ask depth at or below the limit into a separate `paper_fills` row, stores VWAP evidence, keeps the order `PartiallyFilled` until cumulative fills reach the requested size, and settles only actually filled shares/cost basis; any unfilled remainder does not participate in the win/loss. Each variant uses its own synthetic wallet (`strategy:<variant_code>`) so paper positions, settlements, and performance do not merge across variants. After the market end time, the worker looks up closed Gamma metadata, writes a `paper_position_settlements` row for filled entries, zeroes the paper position, and stores final settlement price/value/PnL back on the strategy run row even if that variant has since been disabled. GTD limit runs that never fill before expiration are marked skipped as `gtd_limit_not_filled` instead of being settled as wins or losses. If Gamma outcome/token mapping is missing or the due time is already outside `BtcUpDown5mStrategy:EntryGraceSeconds` for non-`Skip` opening-limit variants, the run is skipped with an explicit reason.

Paper fill accounting deliberately uses the submitted paper limit price as the fill price even when visible book depth or an observed trade is better than the limit. The better observed depth/trade price remains in fill evidence only; this makes Paper PnL stricter for strategy comparison.

BTC due-entry placement is selected globally by `entry_due_at_utc` across enabled non-PreOpen variants, capped by `MaxEntriesPerCycle`, and processed as bounded parallel run-level work items up to `MaxConcurrentEntryDecisions`. Current-market regular and Martin due entries run before the separate PreOpen pass, so opening/delayed strategies for an already-started market are not blocked behind same-timestamp PreOpen orders for a future market. PreOpen fixed-direction entries still have their own batch pass: the worker selects the complete earliest due PreOpen timestamp group with no `MaxEntriesPerCycle` split, so one due market is not smeared across later cycles. Opening-limit stake sizing also caches CLOB `/book` fallback tasks per asset inside a cycle, so a burst of variants sharing the same token does not repeat the same missing/stale book request hundreds of times. BTC due settlement now uses the same global-queue shape across all variants instead of walking one variant at a time: ended `Entered` runs are selected by market end, filled/partially-filled orders are prioritized, the batch is capped by `MaxSettlementsPerCycle`, and work runs concurrently up to `MaxConcurrentSettlements`. Per-cycle Gamma market metadata is cached for settlement so a burst of strategies sharing the same token does not repeat the same closed-market lookup or timeout.

Late-entry BTC GTD orders whose due time is after the market midpoint bypass the market-end safety offset and use the fallback TTL/market-end cap instead, so variants such as `More 270` can still place an order in the final half of a 5-minute market.

The pre-open fixed-direction variants extend the same BTC worker beyond 5-minute markets for the specific BTC `5m`, `15m`, `1h`, and `4h` Up/Down series. They create one run only for the matching market interval, set `entry_due_at_utc` to five minutes before market start, select the configured fixed outcome (`Up` or `Down`), and submit a fixed-price pending Paper GTD BUY even when the selected outcome has no current executable liquidity. Their due placement drains the whole earliest due PreOpen group in one pass instead of being capped by `MaxEntriesPerCycle`, but it runs after same-cycle current-market regular/Martin entries so a future PreOpen batch cannot delay an already-open market's entry. Execution is accounted separately by the Paper GTD fill pipeline: the order is filled only when visible asks at or below the limit, or later high-confidence trade-through evidence, provide matching liquidity while the order is alive. `PreOpen Half` orders use a local cancel deadline at half of the market period after open (`2.5m`, `7.5m`, `30m`, or `2h`), while ordinary `PreOpen Full` orders stay active until the market-end safety deadline (`marketEndUtc - OpeningLimitExpireBeforeMarketEndSeconds`, default one minute before close). `PreOpen Full ... Sell` variants use a fixed-price Full BUY entry without the pre-close local cancel deadline, then in the final quarter of the market compare the current Up/Down order-book direction with the fixed outcome; if it differs, they place a Paper SELL on the filled shares at the visible bid-side price needed to cross immediately. These variants are Paper-only and do not add live order placement.

BTC close-book result inference now starts capturing CLOB `/book` snapshots for active BTC 5-minute markets during the final `BtcUpDown5mStrategy:CloseBookCaptureLookbackSeconds` seconds before close, throttled by `CloseBookCaptureIntervalSeconds`; service config uses 60 seconds and 10 seconds. Skip strategies first try the current close-book fetch, then fall back to the latest stored `order_book_snapshots` row for that token if the book stops responding after close. The result no longer requires a full midpoint: `Up` midpoint still wins at `>= 0.5`, but a single `Up best_bid >= 0.5` also infers `Up`, a single `Up best_ask < 0.5` infers `Down`, `Down best_ask <= 0.5` infers `Up`, and `Down best_bid > 0.5` infers `Down`. If the available one-sided signals conflict, the run is skipped with `btc_close_book_inference_conflict`; if no usable book or stored snapshot exists, it is skipped with close-book diagnostics.

A dedicated BTC order-book refresh worker keeps the shared market-data cache warm for active and near BTC Up/Down strategy markets by polling CLOB `/book` every `BtcUpDown5mStrategy:OrderBookRefreshIntervalMilliseconds` milliseconds, default `1000`. It covers the BTC `5m`, `15m`, `1h`, and `4h` series used by enabled strategy variants, registers the same asset ids with the active WebSocket subscription registry, and stamps REST snapshots with the local receive time before applying them to the cache, so the strategy freshness check measures local cache age rather than an already-old exchange timestamp. This reduces `missing_orderbook_cache_stale` skips while still leaving stale-cache rejections visible when neither WebSocket nor REST refresh can keep up.

When `Middle`, `Middle Revert`, `Skip`, or `Skip Revert` do not yet have enough own settled rows for dynamic break-even pricing, they now bootstrap the GTD BUY limit from the selected outcome order book rather than blocking the first orders. The bootstrap uses `best_ask` when it is at or below `0.50`; otherwise it uses `best_bid + tick`, capped at `0.50`. If neither usable book price exists, the run is skipped with book-bootstrap diagnostics.

`BTC Up or Down 5m Binance` waits until the BTC 5-minute market accepts orders, compares the latest Binance BTC/USDT trade price with the first archived BTC reference for that market from `btc_up_down_5m_odds_ticks`, buys `Up` when current BTC is above start, buys `Down` when current BTC is below start, skips equality, and creates a GTD BUY capped at `0.50` with the same BTC opening-limit expiration policy. `BTC Up or Down 5m Binance 0.1 bps` through `0.9 bps`, plus `1 bps`, `2 bps`, and `5 bps`, keep that baseline price and direction logic but skip unless the absolute BTC move from market start is at least the configured bps threshold; the skip reason is `btc_reference_move_below_bps_threshold`. `BTC Up or Down 5m Binance 15s`, `30s`, and `45s` use the same direction/price rule but wait for the configured delay after market open before reading the current Binance reference. `BTC Up or Down 5m Binance 45`, `47`, and `49` use the same direction signal but submit fixed GTD BUY limits at `0.45`, `0.47`, and `0.49` respectively, so their fill rate and payoff can be compared against the `0.50` baseline. If the archive has not produced a start reference yet, the observed run waits for the next cycle instead of being permanently skipped.

`BTC Up or Down 5m Binance Clever` uses the same Binance start-relative direction, but prices the entry from the odds archive instead of always using `0.50`. It estimates target outcome fair value from recent `btc_up_down_5m_odds_ticks` samples with similar direction-normalized BTC move from market start, similar seconds-to-close, and comparable book quality. The baseline Paper BUY limit is `fair value - 0.03`, discounted for one-sided/wide/non-WebSocket book evidence, capped at `OpeningLimitMaxPrice` / `0.50`, and floored to the configured tick. `BTC Up or Down 5m Binance Clever Aggressive` uses a `0.01` fair-value margin, while `BTC Up or Down 5m Binance Clever Conservative` uses `0.05`; `BTC Up or Down 5m Binance Edge 2/4/6` run the same fair-value model with `0.02`, `0.04`, and `0.06` required edge. It skips when the current market has no archived odds snapshot, the current spread is too wide, the historical sample is under 20 ticks, or the computed safe price is non-positive.

`BTC Up or Down 5m Prev Score Countertrend 10..90` reads only the immediately previous BTC 5-minute market from `btc_up_down_5m_odds_ticks`; it does not score the current market for a current-market entry. For the previous market it computes BTC deviations from the archived Binance start price, winsorizes the deviation tails, and takes a timestamp-duration-weighted average. A positive score means the previous market was biased `Up`, so the next market buys `Down`; a negative score buys `Up`; a score inside `PreviousScoreCounterTrendEpsilonScore` or with fewer than `PreviousScoreCounterTrendMinSamples` skips. The 17 variants share the same previous-market signal but use their own fixed GTD BUY limit prices from `0.10` through `0.90` in `0.05` steps.

`BTC Up or Down 5m Ensemble 2 of 3` votes between Binance start-relative, Middle 1, and Skip 1 and enters only when at least two available votes agree on the same single outcome. `BTC Up or Down 5m Dynamic Markov` estimates the next result from recent BTC 5-minute result transitions and enters only when the conditional next-outcome probability is at least `0.55`. `BTC Up or Down 5m Strategy Selector` ranks selected opening-limit strategies by recent positive Paper expectancy and reuses the best candidate's current signal. None of these strategies place both sides of the same market.

The `BinanceBtcUsdReference` service keeps a live WebSocket connection to `wss://data-stream.binance.vision:443/ws/btcusdt@trade`. The latest trade price is used for immediate BTC Middle decisions, while the rolling cache samples that latest trade once per `BinanceBtcUsdReference:SampleIntervalSeconds`, default `60`, and keeps the latest `BinanceBtcUsdReference:WindowSize` samples, default `100`, in memory. The cache snapshot includes source, latest sampled price, source update time from the trade event, sample count, full-window flag, and arithmetic mean over the retained samples. It is exposed locally through `GET /btc-usd-reference` on the IPC listener. This is a research/reference feed, not the CLOB order-book price used for BTC Paper GTD limits.

`BtcUpDown5mOddsArchive` stores a compact research archive in `btc_up_down_5m_odds_ticks` while BTC 5-minute markets are active. Each tick records the current Binance BTC/USDT reference price, the first archived BTC price for that market, BTC move from that reference, Up/Down best bid/ask/mid or one-sided proxy, quote source (`websocket_cache` or `clob_rest`), quote age, and diagnostics for missing books. It is intended for testing whether BTC movement from market start explains or predicts Polymarket odds, without enabling high-volume global order-book persistence.

`CryptoUpDown5mOddsArchive` extends the same research-only archive pattern to non-BTC crypto 5-minute markets, currently `ETH`, `SOL`, and `XRP`. It stores rows in `crypto_up_down_5m_odds_ticks` with the asset symbol, Binance `<asset>USDT` trade-stream price, the first archived market-start reference, asset move from start, Up/Down book proxy, source/age, and diagnostics. The companion `BinanceCryptoReference` service uses one Binance combined WebSocket stream for those symbols. This data is for a later comparison of liquidity, spread, fillability, and start-relative price/odds correlation; it does not create Paper or Live orders.

## Paper Trading

When Paper runtime is enabled, accepted Follow leader signals create `PaperOrder` records at the exact leader trade price, with proposed size, notional, configured TTL, copied trader wallet, and strategy id. Paper runtime is enabled in `Bot:Mode=Paper`, and can keep running as shadow Paper in `Bot:Mode=Live` when `PaperTrading:RunInLiveMode=true`. BUY and SELL entries no longer chase the current top of book in the signal decision; the pending paper order fills later only if the market trades back through that leader price. When `PaperTrading:UseMinimumMarketOrderSize=true`, the proposed paper size is the current market `min_order_size` from the order book instead of bankroll-sized `$25`/`$12.50` test orders. A dedicated Paper open-order worker runs every `PaperTrading:OpenOrderProcessingIntervalSeconds`, default `5`, to expire stale pending orders, simulate approximate fills from fresh WebSocket order books first, and fall back to observed REST CLOB order books. Fill simulation is batched by `PaperTrading:OpenOrderFillSimulationBatchSize`, default `100`, so a large old backlog cannot block new GTD expiry checks.

For paper BUY orders, a fill is simulated only from executable ask depth at or below the paper buy limit, or from an observed trade with size at or below that limit. The engine records one `PaperFill` per simulated execution, caps each fill to the order's remaining shares, stores balanced depth/trade evidence with the VWAP, and keeps the order partially filled until cumulative fills reach the requested size. Long positions are updated with weighted-average cost and valued using the current bid, not midpoint or ask.

For paper SELL orders, a fill is simulated from executable bid depth at or above the paper sell limit, or from an observed trade with size at or above that limit. SELL fills reduce the matching copied-wallet paper position and store approximate realized PnL on the fill as `(sellPrice - averageEntryPrice) * soldShares`. The remaining position keeps the original average entry price and is marked from the current bid. When minimum-size paper orders are enabled, SELL signals with less than the market minimum remaining in the copied-wallet position are rejected as `paper_position_below_market_minimum`.

For both BUY and SELL paper fills, crossed book/trade evidence determines whether and how many shares can fill; accounting price is the submitted paper limit, not the better visible market price.

WebSocket market-data updates also dispatch into paper trading so pending orders can fill and paper positions can be re-marked without waiting for the next scanner loop. Stale WebSocket snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.

Copied leader exits are tracked by a separate background worker controlled by `PaperTrading:LeaderActivityExitTrackingEnabled`. A filled copied BUY activates its `paper_copied_leader_positions` row with the actual copied paper size. The worker selects due active links, calls Data API `/activity?user=<wallet>` sorted newest first with a timestamp cache-buster, filters `TRADE`/`SELL` rows for the same asset after the copied entry, and writes deduped observations to `paper_copied_leader_activity_events`. For each matched leader sell it creates a proportional paper SELL order priced at the leader's sell activity price: `leader sell size / leader initial copied position size` is applied to our copied paper size and capped by the current available paper position minus already-open SELL orders. Activity rows with invalid prices are skipped. This is still paper-only and does not place live orders.

Paper accounting also settles copied-wallet positions when a market resolution is observed from the market WebSocket or from the periodic closed-Gamma scan. Settlements are written to `paper_position_settlements`, open paper positions are zeroed, and `paper_copied_trader_performance` is rebuilt continuously with `OVERALL` and category rows so the dashboard can prioritize the copied wallets that actually helped our paper account.

## Dry Run Trading

In `DryRun` mode, accepted signals produce CLOB V2 order payloads without sending them to Polymarket. The dry-run path validates tick size, minimum size, price, signature type, signer/funder addresses, order type, and GTD expiration. BUY and SELL amounts are converted with 6-decimal fixed math according to the official V2 order model.

When `PolymarketAuth:DryRunSigningEnabled` is true and `DryRunPrivateKeyName` resolves through the configured secret provider, the app signs the order locally with the V2 EIP-712 domain. If the key is absent, the signer address does not match, or validation fails, the result is stored as `DryRunUnsigned` or `DryRunRejected`. Stored payloads are redacted and no `POST /order`, cancel, or authenticated trading HTTP call is made.

## Live Trading

Live trading is disabled by default. To place any live order, all gates must pass: `Bot:Mode` must be `Live`, `Bot:EnableLiveTrading` must be `true`, `LiveTrading:ManualEnableCode` must equal `LIVE_TRADING_ENABLED`, auth must be configured, geoblock must be clear from the machine running the service, CLOB server time must be within drift limits, no API-error or daily-loss lockout may be active, and the local kill switch/live pause must be clear. `PaperTrading:RunInLiveMode=true` only keeps Paper simulation and settlement running alongside Live; it does not place or authorize live orders.

Follow leader live orders remain BUY-only, GTD-only, post-only, and capped by `LiveTrading:MaxOrderNotionalUsd` plus live bankroll percentages; current Follow leader Paper pricing uses the leader's historical trade price, so the live preflight rejects these signals until a separate live execution policy is explicitly implemented. Standard BTC taker live stakes, if enabled later, submit BUY GTD limit orders with `postOnly=false` and a bounded expiration. The current BTC Paper/Live-shadow opening-limit path for the explicitly allowed BTC variants (`BTC Up or Down 5m Skip 1` and `BTC Up or Down 5m More 150 Below 65`) submits `GTD` BUY limits with `postOnly=false`; by default local cancellation is one minute before market end and the CLOB wire expiration includes `ClobGtdExpirationSecurityBufferSeconds` (`60`). Before placement the service refetches the order book, checks clock drift and risk caps, enforces live bankroll/strategy-balance caps, signs the CLOB V2 payload locally, and sends `POST /order` with L2 headers. Live orders and live events are stored in PostgreSQL; the maintenance loop polls order status and cancels expired/stale orders. The kill switch pauses new live orders and requests cancel-all.

Each strategy also has a live-only `live_available_balance`, default `100.00`, visible and editable as `Live bal` in the Dashboard `Strategies` tab. Live preflight treats existing open live orders for the same strategy as reserved notional. If the remaining strategy balance cannot cover the next live stake, the service logs an error, writes a `StrategyLiveBalance` live event, flips that strategy's `LiveStakes` flag off, and stops placing live orders for that strategy even if the system-wide live caps would still allow trading. Live orders persist accounting fields for average fill price, filled notional, cost basis, fee, settlement value, realized PnL, won/lost flag, and settlement source. For immediately matched CLOB submit responses, BUY accounting derives actual fill price from `makingAmount / takingAmount`, so a GTD limit is treated as a maximum price rather than the realized execution price. When a matched live order can be resolved from closed Gamma metadata, the maintenance loop applies realized live PnL exactly once: winning orders add settlement value minus cost basis, losing orders subtract the cost basis, and the stored balance is clamped at zero. This accounting does not affect Paper trading balances or Paper strategy metrics.

On startup the service checks Polymarket geoblock status from the actual host and writes a `StartupGeoblockCheck` live event. If blocked or if the check fails, live trading is paused.

The Dashboard `Live Readiness` tab shows the current live blockers in one place: config gates, auth readiness, latest dry-run signed order, startup geoblock event, IPC service state, live pause, kill switch, open/stale live orders, API-error and daily-loss lockouts, strategy live-stake funding, and market WebSocket status. It is read-only and does not enable live trading or place orders.

## Analytics And Reporting

The service automatically generates daily reports into `daily_reports` when `Analytics:DailyReportGenerationEnabled` is true. Reports are recalculated every `Analytics:DailyReportRefreshMinutes` for the current UTC day and the previous UTC day.

Dashboard analytics include:

- daily summary: signals observed/accepted/rejected, paper orders, fills, expired orders, paper PnL, open paper exposure, top rejection reasons, API errors;
- trader performance: signal counts, acceptance rate, fill rate, average lag, leader/proposed price comparison, approximate paper PnL, rejection reasons;
- category performance: grouped by `markets.category`, or `unknown` when category is not available;
- execution quality: leader price, proposed price, fill price, price deltas, lag/spread, and bid/ask/mid snapshots after 1m, 5m, and 30m when stored market data exists;
- rejection analysis: reason code counts and share of rejected signals.

CSV export from the dashboard writes `LeaderTrades.csv`, `Signals.csv`, `SignalRejections.csv`, `PaperOrders.csv`, `PaperPositions.csv`, `PaperCopiedTraderPerformance.csv`, `Strategies.csv`, `StrategyRecentPerformance.csv`, `OnChainTrades.csv`, `OnChainParticipants.csv`, and `DailyReports.csv` under `Analytics:CsvExportDirectory`.

Interpret paper results conservatively. Paper fills are approximate, long positions are marked from bid-side data, and historical daily PnL is a generated snapshot over stored paper positions rather than broker-grade accounting. Use the reports to compare filters, traders, categories, and execution quality before considering any live-trading work.

## Dashboard Screens

- Overview: service heartbeat, mode, storage/API status, scanner status, bankroll, exposure, PnL.
- Strategies: all configured strategies, including `Follow leader`, with editable enable/live-stake/live-balance controls, Paper orders, open positions, lifecycle runs, entry-delay health, wins/losses, PnL, live-snapshot ROI, and closed-only ROI.
- Strategies / 24 hours, 6 hours, 1 hour: short strategy slices with recent order/fill/expiry/settlement counts, ROI, average fill price, entry-delay health, and top skip reason.
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
- Live Orders: submitted/live/rejected/cancelled live order records.
- Live Events: live placement, cancellation, polling, and error audit entries.
- Live Readiness: read-only live-session gate checklist showing blockers before any live order can be considered.
- Paper Orders: lifecycle, TTL, fill timestamps, linked signal id.
- Paper Positions: size, average price, estimated value, unrealized PnL.
- Copied Ratings: per copied wallet/category Paper performance used to evaluate followed leaders.
- Market Data: latest WebSocket/market-data asset snapshots, bid, ask, spread, update time.
- Analytics: daily, trader, category, execution-quality, and rejection reports.
- Risk: configured limits and current usage.
- Diagnostics: sanitized config summary, storage status, auth status, service/scanner/WebSocket status, watchlist summary, latest API errors, and risk usage.
- Runbook: local paths and purposes for the operations documents.
- Logs: API errors, risk events, service commands, and market-data events.
- Dashboard Errors: local dashboard refresh, IPC command, and CSV export errors retained in memory with wrapped, copyable details.
- Controls: pause/resume scanner, pause/resume paper/live trading, kill switch, clear kill switch, cancel all live orders, trader discovery, on-chain sync/cancel, on-chain market enrichment, and asset pin/unpin through localhost IPC.
- Trader discovery refresh is also a localhost IPC command and only runs when the operator presses the dashboard button.

## Troubleshooting

- PostgreSQL not configured: set `POLYCOPYTRADER_POSTGRES_CONNECTION` and restart the service. The service does not run with no-op storage.
- Invalid watchlist wallet: the scanner skips placeholder/invalid wallets, records a warning status, and keeps the service running.
- Polymarket TLS certificate errors: configure `Polymarket:CertificatePins` only after verifying the current endpoint certificate pin out of band. Do not use an accept-any certificate callback in production.
- HTTP 429/5xx from Polymarket: public clients retry transient failures according to `Polymarket:MaxRetries` with exponential backoff from `Polymarket:RetryBaseDelayMilliseconds`, default `1000`, and record API errors when retries are exhausted.
- Malformed API response: the failing operation is recorded as an API error; scanner/signal/paper loops continue on later cycles.
- WebSocket disconnected/stale: the market WebSocket reconnects with backoff and stale snapshots are ignored after `MarketDataWebSocket:StaleAfterSeconds`.
- Polygon RPC rejects `eth_getLogs`: lower `OnChainIngestion:MaxBlockRange` or configure `POLYCOPYTRADER_POLYGON_RPC_URL` for a more reliable provider. Public/free RPC endpoints commonly require ranges at or below `10000` blocks; the default is `500`.
- IPC unavailable: check whether `http://127.0.0.1:5118/` is already in use, then run `GET /health` or the QA script runtime smoke.
- Database temporarily unavailable: loop-level error recording is best-effort and will not crash the worker if error persistence also fails.
- VPS backup failing: confirm PostgreSQL client tools are installed and `POLYCOPYTRADER_POSTGRES_CONNECTION` is available to the scheduled task or service account.

Do not enable live trading unless `dotnet build`, `dotnet test`, `--print-config`, runtime IPC smoke, geoblock check from the actual host, and cancel-all testing pass.

## Known Limitations

- API credential bootstrap currently supports Windows Credential Manager storage only.
- Trader enable/disable and cancel selected paper order dashboard buttons are placeholders until command-specific IPC is added.
- On-chain leader scoring is a transparent first pass over resolved positions; it has no current mark-to-market yet.
- The on-chain paper-signal worker depends on timely Gamma market rows, category mappings, Polymarket rating refreshes, and Polygon block polling for fast paper BUY entries. Copied exits depend on Data API `/activity`; if that endpoint lags or omits rows, the paper exit may also lag.
- User-authenticated WebSocket channel is not implemented yet.

## Next Recommended Task

All numbered Codex tasks in `Codex/00_INDEX.md` are implemented. Next work should validate on-chain fills against Data API samples, then tune the on-chain leader score after enough resolved positions accumulate.

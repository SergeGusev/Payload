# Configuration Reference

Configuration lives in `appsettings.json`, environment variables, and secret providers.
Do not commit real credentials.

## Bot

- `Mode`: `ReadOnly`, `Paper`, `DryRun`, or `Live`.
- `PollIntervalSeconds`: main service loop interval.
- `HeartbeatIntervalSeconds`: heartbeat cadence.
- `UseWebSockets`: enables market WebSocket monitoring when true.
- `EnableLiveTrading`: must be true for live trading, but is not sufficient by itself.
- `POLYCOPYTRADER_DEPLOYMENT_VERSION`: optional service-account environment variable that is written into `service_heartbeats.version` as `deploy=<value>`; use it only when the deployment cannot embed a Git commit through `InformationalVersion`.

## LiveTrading

- `ManualEnableCode`: must equal `LIVE_TRADING_ENABLED` for live trading.
- `MaxOrderNotionalUsd`: hard per-order safety ceiling. The service config keeps
  this high enough not to act as the old tiny smoke-test cap; intended stake
  sizing is controlled by each strategy's `Live $` and `Live bal` values.
- `MaxTradeBankrollPct`: per-live-order bankroll safety ceiling.
- `MaxMarketBankrollPct`: per-market live exposure safety ceiling.
- `MaxDailyLossPct`: live daily loss lockout reference.
- `MaxTotalDeployedPct`: live total deployed cap.
- BTC 5-minute live preflight applies market/total deployed caps to open Live
  orders only. Paper exposure is intentionally not counted against these Live
  caps.
- BTC 5-minute live preflight also blocks a new BUY when the same condition
  already has an open Paper or Live BUY on a different outcome.
- `DefaultOrderTtlSeconds`: live GTD order lifetime fallback; must be greater than Polymarket's one-minute GTD security threshold and at most 300 seconds.
- `MaxClockDriftSeconds`: maximum allowed CLOB server-time drift.
- `ApiErrorLockoutCount`: recent Polymarket error threshold.
- `ApiErrorLockoutWindowMinutes`: lockout lookback window.
- `CancelAllOnKillSwitch`: documents intended kill-switch behavior.
- `AutoLivePauseStrategies`: strategy codes or ids where automatic Live-only
  pause is enabled. Default `[]`, meaning no strategy is auto-live-paused by
  recent PnL unless it is explicitly listed. The service config currently leaves
  this list empty, so no strategy participates in automatic Live pause/resume.

## Polymarket

- `DataApiBaseUrl`: public Data API base URL.
- `ClobBaseUrl`: public/authenticated CLOB API base URL.
- `GammaBaseUrl`: Gamma API base URL for active-market ingestion and market metadata enrichment.
- `GeoblockUrl`: geoblock check URL.
- `TimeoutSeconds`: outbound HTTP timeout.
- `MaxRetries`: retry count for transient public API failures.
- `RetryBaseDelayMilliseconds`: base retry delay for transient `429`/`5xx` failures; default `1000`, with exponential backoff on repeated failures.
- `CertificatePins`: optional endpoint-host to SPKI SHA-256 pin map.

`CertificatePins` is supported in both development and production. Keys must be
configured endpoint host names, not full URLs. Values must use
`sha256/<base64-spki-hash>` format.

If a host has no configured pin, standard .NET TLS validation is used. If a host has
pins, the server certificate is accepted only when its Subject Public Key Info hash
matches one of the configured pins and the certificate validity window is current.
This can bypass CA/name validation errors for a known pinned Polymarket key without
accepting arbitrary certificates.

Generate pins from the machine that will run the service:

```powershell
.\scripts\get-polymarket-certificate-pins.ps1
.\scripts\get-polymarket-certificate-pins.ps1 -AsAppSettings
```

Review the printed `Subject` and `Issuer` before trusting a pin. If they do not
belong to Polymarket, the local network or host is intercepting TLS.

Example:

```json
"Polymarket": {
  "CertificatePins": {
    "data-api.polymarket.com": [
      "sha256/<pin-from-current-certificate>"
    ],
    "clob.polymarket.com": [
      "sha256/<pin-from-current-certificate>"
    ],
    "polymarket.com": [
      "sha256/<pin-from-current-certificate>"
    ],
    "ws-subscriptions-clob.polymarket.com": [
      "sha256/<pin-from-current-certificate>"
    ]
  }
}
```

## PolymarketHttpLogging

Controls PostgreSQL persistence for `polymarket_http_logs`. This table is for
incident diagnostics only; strategy execution and Dashboard metrics do not depend
on every successful HTTP call being archived.

- `Enabled`: enables the HTTP log sink. Default `true`.
- `PersistSuccessfulRequests`: when true, persists every successful request.
  Default `false`.
- `SuccessfulRequestSampleRate`: when greater than zero and
  `PersistSuccessfulRequests=false`, persists one successful request out of N.
  Default `0`, meaning no successful-request sampling.
- `PersistNetworkErrors`: persists failures without an HTTP status, such as
  timeouts and network exceptions. Default `true`.
- `PersistRateLimitedRequests`: persists HTTP `429`. Default `true`.
- `PersistAuthFailures`: persists HTTP `401` and `403`. Default `true`.
- `PersistServerErrors`: persists HTTP `5xx`. Default `true`.
- `PersistOtherClientErrors`: persists other HTTP `4xx` responses. Default
  `false`.
- `PersistNotFound`: persists HTTP `404`. Default `false`, because some Gamma
  and CLOB lookups use missing rows as a normal control path.
- `CleanupEnabled`: runs the retention worker. Default `true`.
- `CleanupIntervalMinutes`: interval between cleanup cycles. Default `10`.
- `CleanupBatchSize`: maximum rows deleted per cleanup batch. Default `25000`.
- `CleanupMaxBatchesPerCycle`: maximum cleanup batches per cycle. Default `2`.
- `SuccessfulRetentionHours`: retention for successful/sampled HTTP logs.
  Default `6`.
- `FailedRetentionDays`: retention for failed HTTP logs. Default `14`.

## BinanceBtcUsdReference

The service can keep a live Binance BTC/USDT trade WebSocket open, expose the
latest trade as the current BTC reference price, and sample that latest trade
once per minute into the in-memory arithmetic-mean window used by Middle
strategies.

- `Enabled`: runs the Binance BTC/USDT reference stream service when true; default `true`.
- `StreamUrl`: Binance trade stream URL, default `wss://data-stream.binance.vision:443/ws/btcusdt@trade`.
- `SampleIntervalSeconds`: interval for adding the latest trade to the rolling reference window, default `60`.
- `WindowSize`: number of latest sampled values kept in memory and used for the arithmetic mean, default `100`.
- `StaleAfterSeconds`: maximum latest-trade age accepted by Middle strategies, default `5`.
- `ReconnectBaseDelaySeconds`: initial reconnect delay after stream failure, default `2`.
- `ReconnectMaxDelaySeconds`: maximum reconnect delay after repeated stream failures, default `60`.
- `ReceiveBufferBytes`: WebSocket receive buffer size, default `16384`.

The latest cache snapshot is exposed on the local IPC endpoint
`GET /btc-usd-reference`. The window is in memory, but service startup warms it
from the latest minute-level Binance BTC samples already stored in
`btc_up_down_5m_odds_ticks`, up to `WindowSize` records, before the live stream
continues adding new samples.

## BtcOrderBookLagDiagnostics

When enabled, the service stores event-level lag diagnostics in
`btc_order_book_lag_diagnostic_events`: every received Binance BTC/USDT trade,
Binance REST `bookTicker` snapshot, and Polymarket top-of-book WebSocket update
gets a local receive timestamp, source event timestamp where available, best
bid/ask/mid where available, top-level sizes where available, and calculated
local lag milliseconds. This is for short-window research into whether Binance
ticks or quote changes lead Polymarket book movement.

- `Enabled`: records the diagnostic stream when true; default `false`.
- `FlushIntervalMilliseconds`: buffer flush interval, default `1000`.
- `MaxBatchSize`: maximum rows written per flush, default `1000`.
- `MaxQueueSize`: maximum in-memory queued diagnostic events before dropping,
  default `100000`.
- `RetentionMinutes`: retention window for the diagnostic table, default `180`.
- `CleanupIntervalMinutes`: cleanup interval, default `10`.
- `CleanupBatchSize`: maximum rows deleted per cleanup batch, default `50000`.
- `CaptureBinanceTrades`: records Binance trade events when true, default
  `true`.
- `CaptureBinanceBookTicker`: records Binance REST book-ticker snapshots when
  true, default `true`.
- `BinanceBookTickerUrl`: Binance REST book-ticker endpoint, default
  `https://api.binance.com/api/v3/ticker/bookTicker?symbol=BTCUSDT`.
- `BinanceBookTickerPollIntervalMilliseconds`: book-ticker polling interval,
  default `1000`.
- `BinanceBookTickerTimeoutMilliseconds`: book-ticker HTTP timeout, default
  `2000`.
- `CapturePolymarketTopOfBook`: records Polymarket top-of-book updates when
  true, default `true`.

## BinanceCryptoReference

The service can keep a Binance combined trade WebSocket open for non-BTC
crypto research assets. The current use is ETH/SOL/XRP Up or Down 5m analytics;
it does not place orders.

- `Enabled`: runs the combined trade stream service when true; default `true`.
- `CombinedStreamBaseUrl`: Binance combined stream base URL, default
  `wss://data-stream.binance.vision:443/stream`.
- `AssetSymbols`: base assets tracked against USDT, default `ETH`, `SOL`,
  `XRP`.
- `StaleAfterSeconds`: maximum latest-trade age accepted by the archive worker,
  default `5`.
- `ReconnectBaseDelaySeconds`: initial reconnect delay after stream failure,
  default `2`.
- `ReconnectMaxDelaySeconds`: maximum reconnect delay after repeated stream
  failures, default `60`.
- `ReceiveBufferBytes`: WebSocket receive buffer size, default `16384`.

## BtcUpDown5mOddsArchive

The service can continuously store a compact BTC 5-minute odds archive in
PostgreSQL table `btc_up_down_5m_odds_ticks`. Each row joins the current
Binance BTC/USDT trade-stream price, the first archived BTC price for that
market, and the current Polymarket Up/Down top-of-book price proxy. This archive
is for research and diagnostics; it does not place or modify orders.

- `Enabled`: runs the BTC 5m odds archive worker when true; default `true`.
- `PollIntervalSeconds`: interval between archive attempts, default `5`.
- `MaxMarketsPerCycle`: maximum BTC 5m Gamma markets inspected per cycle,
  default `500`.
- `MaxOrderBookAgeMilliseconds`: maximum accepted WebSocket cache age before
  REST fallback is attempted, default `15000`.
- `RestFallbackEnabled`: when true, uses CLOB `/book` if the WebSocket cache is
  missing or stale, default `true`.

## BtcUpDown5mStatistics

Runs the read-only `BTC Up or Down 5m Statistics` research worker. It polls the
current BTC price during active BTC 5-minute markets, estimates Up/Down
probability from `btc_5m_history` with four-point interpolation, stores decision
ticks in `btc_up_down_5m_statistics_ticks`, and queues live observations for
later application to `btc_5m_history` after the market result is known. It does
not place Paper, dry-run, or live orders.

- `Enabled`: runs the statistics worker when true; default `true`.
- `PollIntervalSeconds`: interval between statistics cycles, default `1`.
- `MaxMarketsPerCycle`: maximum active BTC 5m markets inspected per cycle,
  default `500`.
- `MinHistorySupport`: minimum interpolated historical support required before
  a probability is considered actionable, default `20`.
- `MinimumEdge`: required probability-minus-market-price edge, default `0`.
- `HistorySecondsStep`: `seconds` grid step in `btc_5m_history`, default `5`.
- `HistoryCentsStep`: `cents` grid step in `btc_5m_history`, default `5`.
- `HistoryMaxSeconds`: maximum rounded seconds key used for live observations,
  default `295`.
- `HistorySampleOffsetSeconds`: offset after each rounded second before a live
  observation can be queued, default `2`.
- `MaxOrderBookAgeMilliseconds`: maximum accepted WebSocket cache age before
  REST fallback is attempted, default `15000`.
- `RestFallbackEnabled`: when true, uses CLOB `/book` if the WebSocket cache is
  missing or stale, default `true`.
- `ResultSettlementDelaySeconds`: delay after market end before trying to apply
  queued observations to history, default `30`.
- `ResultRetryDelaySeconds`: retry delay when the closed Gamma result is not
  available yet, default `60`.
- `MaxHistorySettlementsPerCycle`: maximum queued observations settled per
  cycle, default `500`.

## BtcUpDown5mArbitrageScanner

Runs a read-only covered-arbitrage scanner for active BTC 5-minute binary
markets. It does not place Paper, dry-run, or live orders. Each cycle reads Up
and Down ask depth from the shared WebSocket cache or CLOB REST fallback,
computes the best equal-share covered position, and writes diagnostics to
`btc_up_down_5m_arbitrage_scans`. A row is actionable only when
`would_arbitrage=true`; otherwise `decision_code` records why the scanner
skipped it.

- `Enabled`: runs the scanner worker when true; default `true`.
- `PollIntervalSeconds`: interval between scanner cycles, default `1`.
- `MaxMarketsPerCycle`: maximum BTC 5m Gamma markets inspected per cycle,
  default `500`.
- `MaxOrderBookAgeMilliseconds`: maximum accepted WebSocket cache age before
  REST fallback is attempted, default `15000`.
- `RestFallbackEnabled`: when true, uses CLOB `/book` if the WebSocket cache is
  missing or stale, default `true`.
- `MinExecutableShares`: minimum equal shares required on both outcomes before
  an opportunity can be considered, default `5`.
- `MaxExecutableShares`: maximum equal shares evaluated per side, default
  `100`.
- `SafetyBufferPerShare`: per-share discount from guaranteed payout used to
  cover fees, rounding, and execution risk in the read-only calculation,
  default `0.001`.
- `MinNetProfitUsd`: minimum net profit after the safety buffer before
  `would_arbitrage` becomes true, default `0.01`.

## CryptoUpDown5mOddsArchive

The service can continuously store non-BTC crypto 5-minute and 15-minute odds in PostgreSQL
table `crypto_up_down_5m_odds_ticks`. Each row contains the asset symbol, Binance
USDT reference price, first archived market-start reference, asset move from
market start, and Up/Down top-of-book proxy from WebSocket cache or CLOB REST.
This archive is for ETH/SOL/XRP research and diagnostics only.

- `Enabled`: runs the crypto 5m odds archive worker when true; default `true`.
- `AssetSymbols`: Polymarket/Binance base symbols to track, default `ETH`,
  `SOL`, `XRP`.
- `PollIntervalSeconds`: interval between archive attempts, default `5`.
- `MaxMarketsPerCycle`: maximum active Gamma markets inspected per cycle,
  default `500`.
- `MaxOrderBookAgeMilliseconds`: maximum accepted WebSocket cache age before
  REST fallback is attempted, default `15000`.
- `RestFallbackEnabled`: when true, uses CLOB `/book` if the WebSocket cache is
  missing or stale, default `true`.

## ChainlinkBtcUsdDiagnostics

When enabled, the service periodically compares the latest sampled Binance
BTC/USDT reference value with the nearest Chainlink BTC/USD Data Streams
benchmark returned by `data.chain.link` and stores the paired observation in
PostgreSQL table `btc_usd_reference_correlation_samples`.

- `Enabled`: runs the diagnostic worker when true; default `true`.
- `BaseUrl`: Chainlink data site base URL, default `https://data.chain.link`.
- `FeedId`: BTC/USD stream feed id used for diagnostics.
- `PollIntervalSeconds`: interval between comparison attempts, default `10`.
- `TimeoutSeconds`: HTTP timeout for the Chainlink diagnostic request, default `15`.
- `MaxNearestAgeSeconds`: maximum accepted timestamp distance between Binance
  and Chainlink points, default `30`.
- `QueryWindow`: Chainlink query window passed to the live-data endpoint, default
  `1m`.

## PolymarketAuth

Only lookup names belong in config. Secret values belong in environment variables or
Windows Credential Manager.

- `Enabled`: enables auth readiness checks.
- `SecretProvider`: `Environment` or `CredentialManager`.
- `SigningAddress`: wallet that signs EIP-712 messages.
- `FunderAddress`: funded Polymarket wallet/proxy used as maker.
- `ChainId`: Polygon is `137`.
- `SignatureType`: `EOA`, `POLY_PROXY`, `POLY_GNOSIS_SAFE`, or `POLY_1271`. Use
  `POLY_1271` for Polymarket deposit wallets; the order payload signs with the
  funded deposit wallet as `maker` and `signer`, while the configured EOA private
  key still comes from `OrderSigningPrivateKeyName`.
- `DryRunSigningEnabled`: enables dry-run signing if the dry-run key exists.
- `DryRunPrivateKeyName`: lookup name for dry-run key.
- `OrderSigningPrivateKeyName`: lookup name for live order signing key.
- `ApiKeyOwnerName`: lookup name for API key owner UUID.
- `ApiKeyName`: lookup name for API key.
- `ApiSecretName`: lookup name for API secret.
- `ApiPassphraseName`: lookup name for API passphrase.

When `SecretProvider` is `CredentialManager`, CLOB L2 API credentials can be
derived or created by running the service command from the built output
directory:

```powershell
.\PolyCopyTrader.Service.exe --bootstrap-polymarket-api-credentials
```

The command refuses to run while Live mode is enabled, reads the configured
order-signing key from the secret provider, signs the L1 CLOB auth message,
and writes the returned values to the configured Credential Manager targets
without printing secret values.

Two local validation commands are available after credentials are stored:

```powershell
.\PolyCopyTrader.Service.exe --auth-readiness-smoke
.\PolyCopyTrader.Service.exe --clob-authenticated-read-smoke
.\PolyCopyTrader.Service.exe --dry-run-signing-smoke
```

`--auth-readiness-smoke` checks local L2 HMAC/header construction. `--dry-run-signing-smoke`
checks local order EIP-712 signing. `--clob-authenticated-read-smoke` sends a
read-only CLOB `GET /trades` request with L2 headers and does not print the
response body. None of these commands sends a live order.

## Storage

- `Provider`: must be `PostgreSQL`.
- `ConnectionString`: local override; prefer environment variables.
- `ConnectionStringEnvironmentVariable`: defaults to `POLYCOPYTRADER_POSTGRES_CONNECTION`.
- `RequireConfiguredDatabase`: set true when the process must fail if storage is missing. The service requires PostgreSQL even if this is overridden; the dashboard can still run without storage.

## IPC

Keep IPC loopback-only.

- `ListenUrl`: service listener URL.
- `DashboardBaseUrl`: dashboard control URL.

## Execution And Risk

Initial live trading requires:

- `Execution:MakerOnly=true`
- `Execution:AllowTaker=false`
- `Execution:MinLeaderTradeUsd`: minimum leader trade notional for signal eligibility; default `0.10`.

## Signal

`DefaultSignalEngine` uses fresh order book data plus enriched on-chain market and
leader/category performance context when those gates are enabled.

- `RequireKnownMarketCategory`: reject signals when market category is missing or `unknown`.
- `RequireLeaderCategoryPerformance`: reject signals without a matching `(wallet, category)` row in `polymarket_onchain_wallet_category_performance`.
- `MinLeaderCategoryResolvedPositions`: minimum resolved positions for the leader in the category.
- `MinLeaderCategoryResolvedRoiPct`: minimum resolved ROI percentage for the leader in the category.
- `MinLeaderCategoryWinRatePct`: minimum resolved win rate percentage for the leader in the category.
- `MinLeaderCategoryScore`: minimum category performance score.
- `MinLeaderCategorySampleQuality`: minimum sample quality, one of `Thin`, `Low`, `Medium`, or `High`.
- `LeaderCategoryPerformanceStaleAfterHours`: maximum allowed age of the category-performance row.
- `LeaderCategoryPerformanceScore`: score bonus when usable leader/category performance is present.
- `CopiedTraderPerformanceGuardEnabled`: when true, the signal engine also checks our own Paper results for the copied leader before accepting another Follow leader signal.
- `CopiedTraderPerformanceMinSettledPositions`: minimum settled Paper positions before the copied-leader guard can reject a wallet/category or wallet overall row; default `3`.
- `CopiedTraderPerformanceMinTotalPnlUsd`: reject after the minimum sample when our total copied PnL for that row is at or below this value; default `-2`.
- `CopiedTraderPerformanceMinRoiPct`: reject after the minimum sample when our copied ROI is at or below this value; default `-10`.
- `CopiedTraderPerformanceMinScore`: reject after the minimum sample when the local copied-leader score is below this 0-100 threshold; default `35`.
- `CopiedTraderPerformanceScore`: score bonus when a copied leader has enough local Paper sample and passes the local guard; default `10`.

Risk settings cap paper and signal sizing. Live trading also applies `LiveTrading`
caps before submitting orders.

Live trading also checks each strategy row's `live_available_balance`, default
`100.00`. The Dashboard `Strategies` tab exposes this as editable `Live bal`.
Before a live order is placed, open live orders for the same strategy are treated
as reserved notional. If the remaining strategy balance is below the required
live stake, the service logs a `StrategyLiveBalance` error, sets that strategy's
`live_stakes=false`, and stops new live bets for that strategy. Matched live
orders adjust this balance only after closed Gamma metadata identifies the
winner: realized live PnL is added for wins and subtracted for losses. Paper
trading does not use this balance.

Live order response bodies are stored in PostgreSQL `jsonb`. Plain-text CLOB
error bodies, such as temporary service-unavailable messages, are wrapped before
storage so they are still persisted without causing a JSON cast failure.
Paper/Live-shadow persistence failures are logged and cancel the affected
submitted order when possible, but they do not clear the strategy Live flag;
strategy Live is still disabled by explicit risk failures such as insufficient
live balance and critical Paper/Live shadow shape mismatch.

The Dashboard `Live Readiness` tab combines these config values with current
runtime evidence: auth readiness, recent dry-run signing, startup geoblock
event, IPC pause/kill-switch status, open/stale live orders, API-error and
daily-loss lockouts, strategy live balance funding, and market WebSocket status.
It is diagnostic only and does not change any setting or submit live orders.

## PaperTrading

- `RunInLiveMode`: when true, Paper runtime keeps creating, filling, settling, and scoring Paper orders while `Bot:Mode=Live`. Live order placement still requires the separate live gates; default `false`.
- `InitialBankrollUsd`: paper bankroll used by bankroll-sized signal orders and risk displays.
- `DefaultOrderTtlSeconds`: paper order lifetime before expiration.
- `OpenOrderProcessingIntervalSeconds`: interval for the dedicated Paper open-order worker that expires pending GTD orders, simulates fills, and updates paper position marks; default `5`.
- `OpenOrderFillSimulationBatchSize`: maximum non-expired open paper orders that perform order-book fill simulation in one worker cycle; expired orders are still closed immediately, except BTC opening-limit GTD orders with initial executable ask evidence get a conservative immediate-fill check before expiration; default `100`.
- `UseMinimumMarketOrderSize`: when true, accepted paper entry signals use the current order book `min_order_size` as the proposed order size instead of bankroll-sized test orders.
- `SettlementEnabled`: when true while Paper runtime is enabled, the accounting worker checks open paper positions against resolved Gamma markets and writes final settlement PnL.
- `SettlementPollIntervalSeconds`: interval between resolved-market settlement scans; default `60`.
- `CopiedTraderPerformanceRefreshSeconds`: interval for rebuilding the local copied-trader paper performance table; default `30`. The table stores an `OVERALL` row and category rows per copied wallet. Its score is a bounded 0-100 local rating based on our Paper PnL, ROI, win rate, settled sample size, lost positions, and open-position penalty.
- `LeaderActivityExitTrackingEnabled`: when true, runs the background worker that tracks copied leader exits from Data API `/activity`; default `true`.
- `LeaderActivityExitTrackingPollDelayMilliseconds`: pause after a successful exit-tracking cycle; default `1000`.
- `LeaderActivityExitTrackingBatchSize`: maximum active copied leader position links selected per cycle; default `100`.
- `LeaderActivityExitTrackingActivityLimit`: `/activity` rows requested per copied wallet; default and max `500`.
- `LeaderActivityExitTrackingRequestDelayMilliseconds`: optional delay between per-wallet `/activity` requests inside one cycle; default `0`.
- `LeaderActivityExitTrackingErrorDelayMilliseconds`: first retry delay after worker-level errors; default `1000`.
- `LeaderActivityExitTrackingMaxErrorDelayMilliseconds`: maximum exponential retry delay after repeated worker-level errors; default `30000`.

## Watchlist

Each trader rule controls wallet, categories, lag, spread, slippage, leader trade size,
and whether the trader is enabled.

## MarketDataWebSocket

Subscribes to the public market WebSocket by CLOB token/asset id. Active Gamma
markets are still upserted to PostgreSQL, but `SubscriptionScope` decides which
of those markets are registered for WebSocket subscriptions. Registered markets
are added in memory before their page is upserted to PostgreSQL, so new
`clobTokenIds` can be subscribed without waiting for database writes.
The in-memory registry is an `assetId -> market snapshot` cache, not just a set
of ids. It keeps compact decision-relevant fields such as market ids, category,
outcome mapping, active/closed/archived/restricted/order-book flags, liquidity,
volume, best bid/ask, spread, last trade price, order minimum size, price tick
size, and relevant timestamps. It does not keep the full Gamma raw JSON or long
description in memory.
WebSocket book/price/best-bid-ask/last-trade messages update cached pricing
fields on the fly. `market_resolved` removes resolved assets from the active
subscription cache. A completed Gamma full scan removes assets that no longer
appear in the `active=true&closed=false` result set.

- `SubscriptionScope`: semantic market filter for Gamma-discovered WebSocket assets. `AllActiveMarkets` preserves broad active-market monitoring; `BtcUpDown5mOnly` registers only BTC Up/Down 5m markets while still keeping pinned/open order/open position assets subscribed separately.
- `MaxSubscribedAssets`: maximum local subscription count; `0` means unlimited. Prefer `SubscriptionScope` for strategy-specific narrowing because a numeric cap can arbitrarily exclude required BTC assets.
- `SubscriptionRefreshSeconds`: fallback refresh cadence. New active Gamma assets also signal the WebSocket loop immediately.
- `SubscriptionBatchSize`: number of asset ids per WebSocket subscribe/unsubscribe payload; default `1000`.
- `ShardMaxAssets`: target maximum asset ids per market WebSocket shard; default `3000`.
- `MaxShardConnections`: soft cap for shard connection count; default `64`, `0` means unlimited.
- `WatchdogIntervalSeconds`: supervisor cadence for subscription reconciliation and shard health checks; default `10`.
- `WatchdogStaleSeconds`: protocol-stale threshold for reopening an otherwise open shard; default `90`, `0` disables stale restarts.
- `PersistOrderBookSnapshots`: writes WebSocket top-of-book snapshots to `order_book_snapshots` when true; default `false` for all-active-market monitoring.
- `PersistMarketDataEvents`: writes generic WebSocket events to `market_data_events` when true; default `false` for all-active-market monitoring.
- `StatusPersistIntervalSeconds`: minimum interval for unchanged `market_data_status` upserts; default `60`.

The service shards all desired asset ids across multiple WebSocket connections
instead of using one huge all-active subscription. Outcomes belonging to the
same market/condition are kept on the same shard. Shard assignment is stable
while the Gamma full scan discovers later pages: new token ids are dynamically
subscribed into existing shards when capacity is available, instead of
restarting all shards on every page. The supervisor stores the aggregate status
in `market_data_status.component='PolymarketMarketWebSocket'` and stores
individual shard rows as `PolymarketMarketWebSocket:shard-001`,
`...:shard-002`, etc. If a shard closes, fails heartbeat/send/receive, or stays
protocol-stale past `WatchdogStaleSeconds`, only that shard is reopened.

## MarketTradeDiagnostics

Records `last_trade_price` WebSocket events into
`polymarket_websocket_trade_ticks` for throughput and trader-identification
diagnostics when enabled. Initial rows are written with
`trader_match_status=1` (`NotFound`).
The current diagnostic path only records WebSocket hooks: it does not run
background `/trades` lookup, does not fill `trader_wallet`, and does not retry
stored `NotFound` rows. The previous Data API market-trades matcher remains in
code for a later implementation.

- `Enabled`: writes diagnostic trade-tick rows when true; default `false`.
- `MarketTradesLimit`: retained page size for the inactive `/trades` lookup helper; default `1000`.
- `MatchTimestampToleranceSeconds`: retained timestamp tolerance for the inactive composite matcher; default `5`.

Market cache updates from WebSocket `book`, `price_change`, `best_bid_ask`, and
`last_trade_price` messages still run independently of this diagnostic table.

## DataApiTraderIngestion

Continuously samples global Data API `/trades` with a timestamp cache-buster and
extracts `proxyWallet` traders. The global discovery worker only upserts trader
rows; it does not write global trade rows. Slow per-wallet sync and the separate
Polymarket-only rating worker do not block the next global `/trades` poll. This
is read-only research storage and does not feed the signal engine or paper/live
trading.

- `Enabled`: runs the background trader discovery and trader sync workers when true; default `true`.
- `GlobalTradesLimit`: global `/trades` page size; default and effective max `1000`.
- `PollDelayMilliseconds`: delay between successful global discovery polling cycles; default `0`.
- `UserTradesLimit`: per-wallet `/trades?user=...` page size; default `1000`.
- `MaxUserHistoricalOffset`: largest per-wallet offset to request during full/fresh sync; default `3000`.
- `TakerOnly`: sent to Data API for global and per-wallet requests; default `false`.
- `MaxTradersPerCycle`: maximum unique global-batch wallets to upsert per discovery cycle; default `1000`.
- `SyncBatchSize`: number of pending/stale wallets the sync worker processes per batch; default `5`.
- `SyncPollDelayMilliseconds`: delay between successful sync batches; default `1000`.
- `ExistingTraderRefreshIntervalSeconds`: minimum age before a completed trader is eligible for another fresh sync; default `3600`.
- `RefreshPositionsEnabled`: legacy switch for the disabled self-computed Data API current/closed position performance path; default `false`.
- `RefreshPolymarketRatingsEnabled`: runs the Polymarket-only wallet/category rating worker when true; default `true`.
- `PolymarketRatingTimePeriod`: leaderboard `timePeriod` used for wallet/category ratings; default `ALL`.
- `PolymarketRatingOrderBy`: leaderboard `orderBy` used for wallet/category ratings; default `PNL`.
- `PolymarketRatingRefreshIntervalSeconds`: successful wallet rating refresh interval; default `3600`.
- `PolymarketRatingFailureDelaySeconds`: retry delay after a wallet rating refresh failure; default `60`.
- `PolymarketRatingRequestDelayMilliseconds`: optional delay between per-category leaderboard requests for one wallet; default `0`.
- `PolymarketRatingPositionsEnabled`: also enrich wallet/category ratings with aggregate `/positions` and `/closed-positions` snapshots; default `true`.
- `PolymarketRatingCurrentPositionsLimit`: `/positions` page size for rating snapshots; default and documented max `500`.
- `PolymarketRatingMaxCurrentPositionsOffset`: largest `/positions` offset to request for rating snapshots; default `0`, so one current-position page is fetched per wallet refresh.
- `PolymarketRatingClosedPositionsLimit`: `/closed-positions` page size for rating snapshots; default and documented max `50`.
- `PolymarketRatingMaxClosedPositionsOffset`: largest `/closed-positions` offset to request for rating snapshots; default `0`, so one closed-position page is fetched per wallet refresh.
- `MaxPositionRefreshesPerCycle`: caps position/performance refreshes per sync batch; default `1000`, practically bounded by `SyncBatchSize`.
- `CurrentPositionsLimit`: `/positions` page size; default and documented max `500`.
- `MaxCurrentPositionsOffset`: largest `/positions` offset to request; default `10000`.
- `ClosedPositionsLimit`: `/closed-positions` page size; default and documented max `50`.
- `MaxClosedPositionsOffset`: largest `/closed-positions` offset to request; default `100000`.
- `ErrorDelayMilliseconds`: first retry delay after a whole-cycle failure; default `1000`.
- `MaxErrorDelayMilliseconds`: maximum exponential retry delay; default `30000`.

New wallets from the global page are upserted into
`polymarket_data_api_traders` immediately. Existing wallets are updated
immediately when profile fields or `last_trade_timestamp_utc` advance; repeated
seen-only global pages are throttled to avoid rewriting the same row on every
poll. The separate sync worker later gives new wallets a full accessible sync
over `/trades?user=<wallet>` and gives completed wallets a fresh sync from
newest rows until the first row at or before the stored
`last_trade_timestamp_utc` is reached. These pages are used only to
advance the wallet cursor; raw per-wallet trade history is not stored in
PostgreSQL. Because the global Data API page can jump, this worker explicitly
accepts source gaps and is not a gap-free activity stream.

The Polymarket-only rating worker keeps
`polymarket_data_api_wallet_category_ratings` current. It selects due wallets by
`polymarket_rating_next_refresh_at_utc`, reads enabled
`polymarket_category_mappings`, calls `/v1/leaderboard` with `user=<wallet>`,
and stores found/not-found plus Polymarket rank, PnL, volume, and a derived
`leaderboard_pnl_to_volume_pct = pnl / vol * 100` efficiency ratio by
wallet/category/time-period/order. The ratio is not official Polymarket ROI or
percent PnL. When rating positions are enabled, it also fetches the configured
`/positions` and `/closed-positions` pages, maps those positions into the same
local categories, and stores aggregate current, closed, and combined position
counts, cost/value, PnL, and percentage PnL on the same rows. These fields are
page-snapshot aggregates, not raw per-position storage; increasing the max
offsets makes the snapshot deeper but heavier.
Successful refreshes update
`polymarket_rating_refreshed_at_utc` and move the next refresh cursor forward;
failures store `polymarket_rating_last_error`, increment attempts, write
`api_errors`, and retry after `PolymarketRatingFailureDelaySeconds`.

The older self-computed `/positions` and `/closed-positions` performance path is
kept in source as disabled legacy logic. It is not the default rating source for
the new simplified pipeline.

## TraderDiscovery

Uses the public Polymarket Data API leaderboard to research candidate wallets before
adding them to the watchlist. Refresh is manual: the dashboard button calls the
service through localhost IPC.

- `Enabled`: allows the manual dashboard/IPC refresh command when true.
- `Category`: leaderboard category such as `OVERALL`, `POLITICS`, or `WEATHER`.
- `TimePeriod`: `DAY`, `WEEK`, `MONTH`, or `ALL`.
- `RefreshIntervalMinutes`: reserved for future scheduled refresh; not used by the current manual flow.
- `LeaderboardPages`: number of 50-row pages to fetch for each leaderboard mode, max `21`; the manual flow uses both `orderBy=PNL` and `orderBy=VOL`, then merges both appearances into one `trader_leaderboard_snapshots` row per wallet/category/period.
- `CandidatesPerSide`: best-PnL candidates from the PnL window and worst negative-PnL candidates from the volume window to enrich.
- `TradesPerCandidate`: recent trades to fetch for each candidate.
- `PositionsPerCandidate`: current positions to fetch for each candidate.
- `RequestDelayMilliseconds`: delay between Data API requests; defaults to `500` for conservative manual discovery.

## GammaMarketIngestion

Continuously builds the new API-first active-market table from Gamma `/markets`.
This is read-only discovery plumbing and does not place, cancel, or modify orders.

- `Enabled`: runs the background active-market ingestion worker when true; default `true`.
- `PollIntervalSeconds`: pause between ingestion cycles; default `0`.
- `PageLimit`: Gamma page size for `/markets`; default `500`.

Each cycle fetches active, non-closed markets ordered by `createdAt` descending.
The worker always continues through all `offset` pages until Gamma returns an
empty array. New `market_id` rows are inserted into `polymarket_gamma_markets`;
existing rows are updated only when Gamma payload fields actually change. A
cycle does not rewrite unchanged rows just to move `fetched_at_utc`.
For each fetched page, WebSocket asset subscriptions are registered in memory
from market `clobTokenIds` before the page is written to PostgreSQL.
The Gamma table stores decision-relevant market fields including best bid/ask,
spread, last trade price, `orderMinSize`, and `orderPriceMinTickSize`.

## BtcUpDown5mStrategy

Runs the experimental Up/Down strategy family in `Paper` mode only.
The worker observes BTC 5-minute Gamma markets, plus ETH/SOL 5-minute Gamma
markets for the crypto Binance and non-Revert Skip variants, and records one lifecycle row per
market and strategy variant in `strategy_market_paper_runs`. Built-in BTC variants
are standard `Less` and `More` plus comparison `Less Gamma` and `More Gamma` at
30-second steps from 30 to 270 seconds after window start, plus `Middle 1`,
threshold `Middle 1 1..100 bps` and matching `Instant` variants, `Middle 1 Revert`,
threshold `Middle 1 Revert 1..100 bps` and matching `Instant` variants, `Skip 1..5`,
`Skip 1..5 Revert`, threshold `Skip 1..50 bps` in 1 bps increments, matching
`Skip 1..50 bps Instant` variants, fixed `Up 1..50 bps Instant` and
`Down 1..50 bps Instant` variants, `Binance`, threshold `Binance 1..50 bps` in 1 bps increments, matching `Binance 1..50 bps Instant` variants, fixed-price `Binance 45/47/49`, delayed
`Binance 15s/30s/45s`, `Binance Clever`, fair-value `Binance Edge 2/4/6`,
`Prev Score Countertrend 10..90`, `Ensemble 2 of 3`, `Dynamic Markov`, `Strategy Selector`, capped `Less`
comparison variants, capped `More` comparison variants, and capped `More Gamma`
comparison variants. ETH/SOL variants include Binance bps `1..50`, Binance bps
`1..50 Instant`, Skip `1..5`, Skip bps `1..50`, Skip bps `1..50 Instant`,
and fixed `Up 1..50 bps Instant` / `Down 1..50 bps Instant` rows;
ETH/SOL Revert variants are intentionally not seeded. When
`PaperTakerPricingEnabled=false`, `Less` selects the lower-priced Gamma
`outcomePrices` entry, `More` selects the higher-priced entry, and that Gamma
reference remains the Paper BUY entry price. When `PaperTakerPricingEnabled=true`,
the standard non-`Gamma` variants use Gamma for market/outcome/token mapping and
settlement metadata only. The worker evaluates both outcome assets from fresh
CLOB/WebSocket executable depth, with REST CLOB `/book` fallback when cached
depth is missing or stale, computes executable ask-depth BUY VWAP for currently
available executable asks, then selects `Less` as the lower executable VWAP and `More` as the higher
executable VWAP. If a candidate book exists but has no executable asks, the
worker creates a resting GTD BUY limit from the Gamma reference plus
`PaperTakerMaxReferenceSlippage` instead of skipping immediately. Capped `Less ... Below ...` and `More ... Below ...` variants
keep the standard CLOB-first selector at their respective delays, but they place
a GTD limit BUY at the configured `Below` cap instead of requiring
immediate executable liquidity below that cap. The
`Gamma`-suffixed variants intentionally use the older
Gamma-first selector for comparison: `Less Gamma` selects the lower Gamma
outcome price, `More Gamma` selects the higher Gamma outcome price, and the
selected asset then uses the CLOB/WebSocket quote as the GTD limit seed, or the
same resting-limit empty-ask fallback when the selected book has no asks. The
`More ... Gamma Below ...` variants keep that Gamma-first selection but place
a GTD limit BUY at the configured cap. The Gamma variants are Paper comparison strategies and are not submitted to live
trading. CLOB/WebSocket/REST prices are trusted for BTC taker Paper; CLOB/Gamma
drift remains diagnostic and is not a skip reason. The run is skipped if a
needed order book is missing/stale, spread is too wide on available executable quotes, the submitted
target size is below market minimum, both executable prices are tied, or a standard selected
executable side violates the boundary: `Less` stays below `0.5`, and `More`
stays above `0.5`. `paper_orders.raw_decision_json` stores the selected entry
quote, source, age, top of book, top depth, Gamma reference, max price,
estimated VWAP, reserved target notional, `order_execution_mode=GTD`, `limit_price`, and an `outcome_selection_source` of
`clob_executable_vwap`, `clob_resting_limit`, or `gamma_outcome_price`; GTD variants store
the final order diagnostics with `order_execution_mode=GTD`,
`opening_limit_price_mode`, and `limit_price`.
When a BTC taker run skips with quote/order-book context, the lifecycle row's
`skip_diagnostics_json` stores cache status, REST `/book` usage, quote age,
top bid/ask depth, executable-depth flags, and both outcome candidates so
non-empty-book liquidity gaps can be diagnosed after the fact.
The extra
`BTC Less 180 Martin` variant uses the 180-second `Less` outcome, waits for
fresh consecutive settled losses from the standard `BTC Up or Down 5m Less 180`
strategy, and then applies a bounded paper stake progression. It later settles
each run from closed Gamma metadata and writes final PnL.

The active non-Instant `Middle` variants use opening-limit pricing rather than
taker pricing. At market open `Middle 1` reads the latest Binance BTC/USDT
trade-stream price and compares it to the Binance cache arithmetic mean. If the
latest trade is above the mean, the strategy buys `Down`; if it is below, it
buys `Up`; equality skips the run.
`Middle 1 Revert` inspects the same reference value and inverts that final
decision: above mean buys `Up`, and below mean buys `Down`. The old `Middle 2`
through `Middle 5` depths, including their bps and revert-bps variants, are no
longer seeded as active strategies; existing rows are retired by schema
initialization. The `Middle 1 1..100 bps` rows keep the same direction logic but
skip unless the absolute latest-trade deviation from the arithmetic mean reaches
the configured threshold; otherwise the run skips with
`btc_reference_mean_deviation_below_threshold`. Matching `Instant` Middle bps
rows keep the same signal and threshold gate, then price the selected outcome
from executable ask depth using the same instant sizing/pricing path and
`InstantOpeningLimitMaxPrice` cap as Binance instant variants. The `Skip` variants inspect the exact immediately previous 5-minute windows without gaps, but they infer those results from close-book
CLOB price evidence instead of waiting for Gamma settlement. The worker captures
`/book` snapshots for active BTC strategy markets and ETH/SOL 5-minute or 15-minute markets during the final
`CloseBookCaptureLookbackSeconds` seconds before close, throttled by
`CloseBookCaptureIntervalSeconds`, and can use the latest stored snapshot for a
token if the book stops responding after close. A full `Up` midpoint still maps
`>= 0.5` to `Up` and `< 0.5` to `Down`, but one-sided evidence is also accepted:
`Up best_bid >= 0.5` means `Up`, `Up best_ask < 0.5` means `Down`,
`Down best_ask <= 0.5` means `Up`, and `Down best_bid > 0.5` means `Down`.
Conflicting one-sided signals skip with `btc_close_book_inference_conflict`; no
usable current or stored book skips with close-book diagnostics. After `N`
consecutive inferred `Up` results the `Skip` variants buy `Down`; after `N`
consecutive inferred `Down` results they buy `Up`; otherwise they skip. The `Skip Revert`
variants inspect the same result stack and invert that final decision:
consecutive `Up` buys `Up`, and consecutive `Down` buys `Down`. `Skip 1..50
bps` variants inspect the immediately previous close-book result, walk backward
through the current streak of identical inferred outcomes, sum each streak
market's archived Binance BTC start-to-close absolute bps move, buy the opposite
outcome, and require that cumulative streak move to reach the configured
threshold. A completed below-threshold cumulative move skips with
`btc_previous_market_move_below_bps_threshold`. Standard `Skip bps` variants use
a fixed `0.50` GTD BUY, while matching `Instant` variants use the same selected
outcome executable ask-depth pricing and sizing path as Binance instant variants.
The shared Skip bps streak calculation also records one
`btc_up_down_5m_result_streak_diagnostics` row per target market. Use
`close_book_streak_result_count` to find the longest same-outcome run and
`cumulative_abs_move_bps` to find the maximum accumulated BTC move over the run.
`BTC Up or Down 5m Up 1..50 bps Instant` and `BTC Up or Down 5m Down 1..50 bps
Instant` reuse that same previous-result streak and cumulative BTC move gate,
but keep only one fixed countertrend side. The former `BTC Up or Down 15m
Up/Down 1..50 bps Instant` rows were removed from production and are no longer
seeded because current 15-minute liquidity/volume is too thin for Live use. `Up` enters only when the Skip bps
countertrend decision is `Up` after a `Down` streak; `Down` enters only when the
countertrend decision is `Down` after an `Up` streak. The opposite side skips
with `btc_previous_market_move_fixed_outcome_mismatch`, and accepted entries use
the same executable ask-depth Instant pricing and `InstantOpeningLimitMaxPrice`
cap as other Instant opening-limit variants.
ETH/SOL Skip rows mirror the non-Revert BTC Skip behavior: plain `Skip 1..5`
uses ETH/SOL close-book result streaks, `Skip bps` uses ETH/SOL close-book
streaks plus archived `crypto_up_down_5m_odds_ticks` start-to-close Binance
move, and `Skip bps Instant` uses the same executable ask-depth pricing path as
the BTC Instant variants. `ETH/SOL Up 1..50 bps Instant` and `ETH/SOL Down
1..50 bps Instant` reuse that same crypto streak/move gate but enter only when
the Skip bps countertrend decision matches the fixed side; the opposite side
skips with `btc_previous_market_move_fixed_outcome_mismatch`. The former ETH/SOL
15-minute fixed Up/Down bps Instant rows were removed from production and are no
longer seeded. ETH/SOL Skip
Revert rows are not seeded. Seeded ETH/SOL Skip/fixed rows can enter the
Paper/Live-shadow path when their Dashboard `Live` flag is enabled and normal
live gates pass.
Temporary ETH Skip guard: if an ETH `Skip`, `Skip bps`, or `Skip bps Instant`
row selects countertrend `Up`, the run is skipped before Paper, Live-shadow, or
Live order creation with `eth_skip_up_direction_temporarily_disabled`. Temporary
SOL Skip 42 guard: if `SOL Up or Down 5m Skip 42 bps Instant` selects
countertrend `Up`, the run is skipped before Paper, Live-shadow, or Live order
creation with `sol_skip_42_up_direction_temporarily_disabled`. Other SOL Skip
rows and fixed ETH/SOL `Up/Down bps Instant` rows are not affected by these
guards.
`Middle`,
`Middle Revert`, `Skip`, and `Skip Revert` create pending Paper BUY orders as
ordinary GTD limit orders. Their limit
price is dynamic by default: the worker reads recent settled runs for the same
strategy, computes `wins / settledRuns`, subtracts
`OpeningLimitBreakEvenMargin`, caps the result at `OpeningLimitMaxPrice`
(`0.50` by default), and floors it to `OpeningLimitPriceTickSize`. If there are
fewer than `OpeningLimitBreakEvenMinSettledRuns` settled rows, the worker first
bootstraps from the selected outcome order book: `best_ask` at or below `0.50`
is used directly; otherwise `best_bid + tick` is used with a `0.50` cap. If the
book does not contain a usable price, or the resulting limit is not positive,
the run is skipped with explicit diagnostics. Until a new `Middle Revert` or
`Skip Revert` variant has enough own settled rows, it first bootstraps dynamic
pricing from the paired base strategy history by treating base losses as
estimated Revert wins; if that sample is also insufficient, it uses the same
order-book bootstrap. The
`BTC Up or Down 5m Up` and `BTC Up or Down 5m Down` wait until the market is
actually accepting orders with an order book, then place a GTD BUY at
fixed price `0.45` on the corresponding outcome. The
`BTC Up or Down 5m Up Maker` and `BTC Up or Down 5m Down Maker` variants use a
maker-style paper decision path and are grouped under `BTC Up/Down 5m Maker`. After a BTC 5-minute
market starts, each variant baselines the selected outcome best ask, tracks a
fixed high-water best ask in memory, and evaluates entries only on 30-second
slots (`30s` through `270s`, maximum 9 attempts per BTC 5-minute market). On a
slot it creates a minimum-size post-only GTD BUY one tick below the current best
ask only when that ask exceeds the previously fixed high-water value. `BTC Up or
Down 5m Up Maker 50` and `BTC Up or Down 5m Down Maker 50` use the same
high-water/slot rules, but place at fixed `0.50` and only when the selected
outcome best ask is strictly above `0.50`. The high-water value is updated only
when a Maker paper order is actually created; between-slot book moves and
no-order slots do not raise it. Flat or falling asks do not create orders; after
a Maker order at `0.55` and a fall to `0.52`, the next Maker order waits until a
later 30-second slot where the ask crosses `0.55` and reaches a new high such as
`0.56`. These Maker orders can happen multiple times in one market, expire at
`marketEndUtc`, and can run
whenever Paper runtime is enabled, including `Bot:Mode=Live` with
`PaperTrading:RunInLiveMode=true`; they only create Paper orders, never submit
Live/Paper-shadow orders, and are intentionally excluded from the
opposite-outcome open-order guard. The
`BTC Up or Down 5m Binance` variant also waits for the market to accept orders,
reads the latest Binance BTC/USDT trade-stream price and the archived market
start reference from `btc_up_down_5m_odds_ticks`, then buys `Up` when current
BTC is above start and `Down` when current BTC is below start. Equality skips,
and the order is a GTD BUY capped at `0.50`. If the archived start
reference is not available yet, the observed run waits for the next processor
cycle instead of being permanently skipped. `BTC Up or Down 5m Binance` bps variants
from `1 bps` through `50 bps` in `1 bps` increments use the same direction and `0.50` GTD limit, but skip with
`btc_reference_move_below_bps_threshold` until the absolute BTC move from market
start reaches the configured bps threshold. The matching `Instant` bps variants keep the same signal and bps threshold, but price the GTD BUY from the selected outcome's current executable ask depth so the initial Paper order is marketable for the computed order size. `BTC Up or Down 5m Binance 15s`,
`30s`, and `45s` use the same start-relative signal and `0.50` cap but wait for
the configured delay after market open before reading the current Binance
reference. `BTC Up or Down 5m Binance Clever`
uses the same start-relative direction, but estimates a target outcome fair
value from recent `btc_up_down_5m_odds_ticks` samples with similar
direction-normalized BTC move from market start, similar seconds-to-close, and
comparable book quality. Its BUY limit is `fair value - 0.03`, discounted for
one-sided/wide/non-WebSocket book evidence, capped at `OpeningLimitMaxPrice` /
`0.50`, and floored to the configured tick. It skips when the current market
has no archived odds snapshot, the current target spread is too wide, the
archive sample has fewer than 20 comparable ticks, or the computed safe limit is
not positive. The `Binance 45/47/49` variants use the same Binance direction
signal with fixed GTD BUY limits at `0.45`, `0.47`, and `0.49`; `Binance Clever
Aggressive` and `Binance Clever Conservative` use the same fair-value model with
`0.01` and `0.05` safety margins; `Binance Edge 2/4/6` use `0.02`, `0.04`, and
`0.06` required fair-value edge. `Prev Score Countertrend 10..90` reads the
immediately previous BTC 5-minute market from `btc_up_down_5m_odds_ticks`,
computes a time-weighted average BTC deviation from that previous market's
start price, winsorizes deviations before averaging, and then enters the next
market against the previous bias: previous `Up` buys `Down`, previous `Down`
buys `Up`, and neutral or insufficient samples skip. Each `10..90` variant uses
the same previous-market score but its own fixed GTD BUY limit price from
`0.10` to `0.90` in `0.05` steps. `Ensemble 2 of 3` votes between Binance
start-relative, Middle 1, and Skip 1 and enters only when at least two available
votes agree on the same single outcome. `Dynamic Markov` estimates the next
result from recent BTC 5-minute result transitions and enters only when the
conditional next-outcome probability is at least `0.55`. `Strategy Selector`
ranks selected opening-limit strategies by recent positive Paper expectancy and
reuses the best candidate's current signal. These non-Maker variants can record
both sides of the same Polymarket market in Paper; the opposite-outcome guard is
enforced only by Live preflight against open Live BUY orders in the same
condition. The
order size still targets the current market minimum passing size plus a `10%`
safety buffer times the configured Paper stake multiplier; diagnostics record
`post_only=false` plus the selected pricing model inputs, cap, final limit, GTD
expiration mode, local cancel deadline, CLOB wire expiration, and fallback
`OpeningLimitGtdTtlSeconds` (`120` by default). They
do not create immediate fills. When the strategy's Dashboard `Live` flag is
enabled, opening-limit entries can create linked live-shadow orders through the
controlled Paper/Live-shadow path if all normal live gates pass. The
generic Paper open-order pipeline then applies balanced GTD
accounting: visible ask depth at or below the limit creates partial `paper_fills`
rows with VWAP evidence, cumulative fills determine `PartiallyFilled` versus
`Filled`, and settlement uses only actually filled shares/cost basis. GTD limit
orders that never fill before expiration are marked `gtd_limit_not_filled` instead of being
counted as won or lost.

`PreOpen Full ... Sell` variants use a fixed-price Full BUY entry without the
pre-close local cancel deadline used by ordinary Full variants. During the final
quarter of the market they compare current Up/Down order-book direction with
the fixed entry outcome; if it differs, they submit a Paper SELL for the filled
shares at a marketable bid-side limit. The exit is still paper-only and is
filled by the ordinary Paper open-order pipeline.

The dashboard `Strategies` tab reads all rows from `strategies`, including
`follow_leader`, and aggregates Paper orders, positions, settlements, and
strategy run lifecycle counters so the BTC variants can be compared against the
current leader-following strategy. It also aggregates Live outcome accounting
from `live_orders` separately from Paper metrics: order/fill/open/settled
counts, won/lost counts, settled cost basis, realized PnL, win/loss rate,
average win/loss, profit factor, expectancy, ROI, and latest live order and
settlement timestamps. The visible grid uses strategy `Name` rather than
internal `Code`, and exposes `MtM PnL` / `MtM ROI %` over realized plus open
unrealized PnL beside `Closed ROI %` over realized PnL divided by already
closed/settled stake.
It also shows decision-health entry delay metrics (`Avg delay s` and
`Max delay s`) computed as actual `entered_at_utc` minus planned
`entry_due_at_utc` for runs that placed a stake. Closed-outcome quality metrics (`Avg win`, `Avg loss`,
`Profit factor`, and `Expectancy`) next to `Win %` so count-based hit rate can
be compared against actual payoff size. Run-based Live condition, technical, and
GTD-unfilled skip counters start at `strategies.live_enabled_at_utc`, so turning
on the manual `Live` flag does not move older pre-Live skipped runs into the
Live skip columns. The nested `24 hours`, `6 hours`, and
`1 hour` tabs under `Strategies` use the same strategy refresh cache and show recent
orders, filled/expired/open orders, entered/skipped/settled runs, wins/losses,
realized PnL, ROI, average fill price, entry-delay health metrics, and the top skip reason. The `Strategies` tab lets `Paused`, `Paper $`, `Live $`,
`Paper Lost`, `Paper Cnt`, `Live Lost`, `Live Cnt`, and live-only `Live bal` be edited for each strategy; for BTC 5-minute
strategies the Paper/Live stake values are interpreted as stake multipliers.
`Paper Lost` and `Live Lost` are persisted as `strategies.paper_lost_coeff` and
`strategies.live_lost_coeff`, both defaulting to `1.00` and constrained to at
least `1`. `Paper Cnt` and `Live Cnt` are persisted signed counters in
`strategies.paper_lost_counter` and `strategies.live_lost_counter`, both
defaulting to `0`. When `Paper Lost` or `Live Lost` is greater than `1`, losses
increment that mode's counter and wins decrement it by `1` even below zero. The
stake add-on applies only while the matching counter is positive:
`Stake * min(Cnt, 2)` is added to the already computed Paper or Live stake at
entry time, so the final stake is capped at three original stakes.
The strategy grids include `Only positive`, `Enabled only`, `Live only`,
`Big ROI`, and `Big settles` filters. `Live only` keeps rows whose manual Live
flag is enabled, even when `Auto Live Pause` is currently suppressing effective
Live entries. `Big ROI` keeps rows with ROI greater than `10` (`Closed ROI` in
`All`, recent `ROI` in the period tabs). `Big settles` keeps rows whose settled
count is greater than `100` (`Settled` positions in `All`, recent `Settles`
runs in the period tabs).
The `Enabled` checkbox writes `strategies.enabled` immediately, and the `Paused`
checkbox writes `strategies.paused`. The service refreshes enabled, manual pause,
and auto Live pause state through a short in-memory cache, so disabled strategies
stop creating new Follow leader signals or BTC 5-minute entries without a
restart, while manually paused strategies stay enabled but skip new Paper and
Live entries with reason `strategy_paused`. Existing Paper positions can still
be settled, and copied leader exits can still be tracked.
The Dashboard `Paper orders` and `Live orders` tabs load only the first recent
orders page; when a strategy is selected, PostgreSQL applies that strategy filter
before the page limit.

Automatic strategy pausing is Live-only and opt-in per strategy through
`LiveTrading:AutoLivePauseStrategies`; an empty list disables automatic Live
pause for every strategy. The service config currently leaves this list empty,
so automatic Live pause is disabled globally and startup clears any stored
`auto_live_paused=true` rows. Entries may be strategy codes such as
`follow_leader` or strategy ids. If strategies are later allowlisted again,
pause and resume use different evidence. After a Live settlement,
the service checks that strategy's settled Live orders over the last 12 hours.
If more than one Live bet exists and the 12-hour Live realized PnL is negative,
it sets `strategies.auto_live_paused=true`, stores
`auto_live_paused_at_utc`, and stores `auto_live_pause_window_start_utc` as the
start of the 12-hour Live-loss window; Live settlements never clear the flag.
The strategy keeps creating Paper entries, and after each Paper settlement the
service checks all settled Paper rows from the stored Live-loss window start
through the current settlement. If that anchored Paper realized PnL is positive
and at least one of those Paper settlements happened after
`auto_live_paused_at_utc`, it clears `strategies.auto_live_paused` and Live
entries resume if the manual `Live` flag is still enabled. Paper settlements
never set the flag. The
Dashboard `Paused` checkbox remains a manual full Paper+Live pause, while
`Auto Live Pause` is read-only state for the automatic Live gate.

- `Dashboard:RefreshIntervalSeconds`: UI refresh timer for the Dashboard; default `60`.
- `Dashboard:StrategyRefreshIntervalSeconds`: minimum interval between Dashboard strategy-performance database refreshes; default `60`. Strategy toggle/stake commands invalidate the cache so command results are shown immediately.
- `Dashboard:StrategiesOnlyMode`: when true, the Dashboard reads service heartbeat plus strategy performance grids and still shows the local `Dashboard Errors` tab for copied/saved refresh, command, export, and strategy edit failures; default `true`. Heartbeat staleness is evaluated against the selected PostgreSQL server clock, not the Dashboard machine clock.
- `Dashboard:OptionalReportTimeoutSeconds`: timeout for optional Dashboard analytics report grids; default `8`. Used only when `Dashboard:StrategiesOnlyMode=false`. If a report times out, the Dashboard keeps the main refresh alive and shows a Diagnostics warning for the skipped report.

- `Enabled`: runs the paper-only BTC 5-minute strategy worker when true; default `true`.
- `PollIntervalSeconds`: worker loop delay; default `1` in the service config to reduce BTC entry timing drift.
- `StakeUsd`: fallback/default BTC stake multiplier; default `1.00`. When fresh market `min_order_size` is available, BTC Paper and Live stake notional is computed as the minimum passing order notional plus `10%`, multiplied by the strategy's Paper or Live stake value, then rounded up to the next whole USD.
- `EntryGraceSeconds`: maximum late-entry grace after a variant's due time before the run is skipped; default `60`. Strict `Skip` / `Skip Revert` decisions no longer wait for Gamma settlement: they infer each immediately previous BTC 5-minute result from close-book CLOB price evidence. Full `Up` midpoint maps `>= 0.5` to `Up` and `< 0.5` to `Down`; single-sided `Up best_bid >= 0.5`, `Up best_ask < 0.5`, `Down best_ask <= 0.5`, and `Down best_bid > 0.5` are also decisive. If current close-book fetch stops responding, the worker uses the latest stored snapshot for that token when available. Missing or conflicting evidence is skipped with diagnostics in `skip_diagnostics_json`.
- `MaxMarketsPerCycle`: maximum BTC 5-minute Gamma markets observed per cycle; default `500`.
- `MaxEntriesPerCycle`: maximum due entries processed per cycle across variants; default `3000`. Regular due-entry selection expands the final batch boundary to include every run with the same `entry_due_at_utc`, so a single market-open timestamp is not split across cycles. Middle reference opening-limit variants also get a shared fast skip pass before generic order placement, skipped runs are batch-updated through one repository call, and accepted BTC Paper signal/order rows are inserted through one transactional repository call.
- `MaxConcurrentEntryDecisions`: maximum due-entry decision work items processed concurrently after the shared fast paths; default `64` in the service config.
- `MaxSettlementsPerCycle`: maximum due settlements selected per cycle from the global settlement queue across variants; default `250`.
- `MaxConcurrentSettlements`: maximum due settlement work items processed concurrently; default `16` in the service config.
- `MartinTriggerLosses`: fresh consecutive losses required from standard `BTC Up or Down 5m Less 180` before `BTC Less 180 Martin` starts; default `3`.
- `MartinStakeLevels`: number of stake levels in the Martin progression; default `1`, so Martin also uses the base stake multiplier without escalating.
- `MartinStateLookbackRuns`: recent settled run depth used to reconstruct Martin trigger/progression state; default `50`.
- `PaperTakerPricingEnabled`: when true, BTC Paper entries use fresh CLOB/WebSocket/REST ask-depth VWAP to seed GTD limit price instead of using Gamma as the fill price; default code value `false`, service config currently sets `true`.
- `PaperTakerRestFallbackEnabled`: when true, fetches CLOB `/book` before rejecting a missing/stale/incomplete WebSocket depth cache; default `true`.
- `PaperTakerMaxQuoteAgeMilliseconds`: maximum age for BTC Paper taker quote/depth; default `1500`.
- `PaperTakerMaxEntryPrice`: absolute cap for any BTC Paper taker BUY; default `0.80`.
- `PaperTakerMaxReferenceSlippage`: maximum resting-limit uplift above each outcome's Gamma reference when executable ask depth is absent; executable quotes can still seed a higher GTD limit through the temporary best-ask allowance.
- `PaperTakerMaxSpreadAbs`: maximum absolute bid/ask spread accepted for BTC Paper taker entries; default `0.10`.
- `PaperTakerMaxGammaClobDiff`: legacy diagnostic threshold retained in config; BTC taker Paper now records Gamma/CLOB drift but does not skip solely because of it.
- `OpeningLimitDynamicBreakEvenPricingEnabled`: when true, `Middle`, `Middle Revert`, `Skip`, and `Skip Revert` GTD limit prices are derived from each strategy's own recent settled win-rate; default `true`.
- `OpeningLimitBreakEvenLookbackRuns`: maximum settled runs read for the dynamic opening-limit win-rate; default `100`.
- `OpeningLimitBreakEvenMinSettledRuns`: minimum settled runs required before a dynamic `Middle`/`Middle Revert`/`Skip`/`Skip Revert` break-even price is trusted; before that, opening orders use the selected outcome order-book bootstrap; default `30`.
- `OpeningLimitBreakEvenMargin`: safety amount subtracted from `wins / settledRuns` before placing `Middle`/`Middle Revert`/`Skip`/`Skip Revert` opening-limit orders; default `0.10`.
- `OpeningLimitMaxPrice`: maximum `Middle`/`Middle Revert`/`Skip`/`Skip Revert` opening-limit BUY price after the break-even margin; default and maximum `0.50`.
- `OpeningLimitPriceTickSize`: tick used to floor dynamic `Middle`/`Middle Revert`/`Skip`/`Skip Revert` opening-limit prices; default `0.01`.
- `OpeningLimitGtdTtlSeconds`: fallback lifetime for BTC opening-limit GTD Paper and Paper/Live-shadow orders when market-relative expiration is disabled or market end is unavailable; default `120`, valid range `30..300`.
- `OpeningLimitExpireBeforeMarketEndSeconds`: local BTC opening-limit GTD cancel deadline offset from market close; default `60`, valid range `0..300`, with `0` disabling the market-relative deadline and falling back to `OpeningLimitGtdTtlSeconds`.
- `ClobGtdExpirationSecurityBufferSeconds`: extra seconds added to the CLOB wire `expiration` for GTD orders so the local effective deadline honors Polymarket's GTD security threshold; default `60`, valid range `60..300`.
- `PreviousScoreCounterTrendEpsilonScore`: minimum absolute previous-market time-weighted score required before `Prev Score Countertrend` enters; default `0.0001`.
- `PreviousScoreCounterTrendMinSamples`: minimum archived BTC samples required from the immediately previous 5-minute market; default `10`.
- `PreviousScoreCounterTrendWinsorPercent`: lower/upper tail percentage used to winsorize previous-market deviations before averaging; default `0.10`, valid range `0..<0.50`.
- `PreviousScoreCounterTrendEnableTimeShareFilter`: when true, requires the previous-market bias to also meet the configured positive/negative duration share before entering; default `false`.
- `PreviousScoreCounterTrendMinUpTimeShare`: minimum positive-deviation duration share for a previous `Up` bias when the time-share filter is enabled; default `0.50`.
- `PreviousScoreCounterTrendMinDownTimeShare`: minimum negative-deviation duration share for a previous `Down` bias when the time-share filter is enabled; default `0.50`.
- `CloseBookCaptureLookbackSeconds`: how long before BTC 5-minute market close the worker starts saving close-book snapshots for result inference; default `60`, use `0` to disable capture.
- `CloseBookCaptureIntervalSeconds`: minimum seconds between close-book snapshot fetches for the same token during the capture window; default `10`.
- `OrderBookRefreshWorkerEnabled`: enables a dedicated BTC 5-minute order-book refresh loop that keeps the shared market-data cache warm from CLOB `/book`; default `true`.
- `OrderBookRefreshIntervalMilliseconds`: delay between refresh cycles; default `1000`.
- `OrderBookRefreshMaxMarketsPerCycle`: maximum active/near BTC 5-minute markets refreshed per cycle; default `4`.
- `OrderBookRefreshMarketLookaheadSeconds`: include markets whose start time is within this future window; default `90`.
- `OrderBookRefreshMarketBehindSeconds`: keep refreshing recently closed/ending markets inside this trailing window; default `30`.
- `OrderBookRefreshRequestTimeoutSeconds`: per-asset CLOB `/book` timeout for the refresh worker; default `5`.
- `EnabledVariantCodes`: optional config-level allowlist of built-in variant codes; empty means all built-in BTC strategy variants are eligible, subject to the runtime `strategies.enabled` flags.

## OnChainIngestion

Reads Polymarket `OrderFilled` events from Polygon through JSON-RPC. This is a
background research workflow, not trading logic. The dashboard `Onchain sync`,
`Enrich markets`, and `Cancel onchain` buttons call the same processors through
localhost IPC for manual forcing and diagnostics. Progress is checkpointed after
every completed block batch.

- `Enabled`: allows on-chain background workers and manual refresh commands when true.
- `TradeCaptureEnabled`: runs the lightweight `OrderFilled` tailer even while the older full on-chain pipeline remains disabled; default `true`.
- `TradeCapturePersistCaptures`: persists decoded tailer rows to `polymarket_onchain_trade_captures`; default `true`, but the low-latency service config sets it to `false` so only cursors and paper outcomes are stored.
- `TradeCaptureSkipStaleCursor`: skips an old tailer cursor forward to the recent block window instead of replaying historical capture backlog; default `false`, enabled in the low-latency service config.
- `TradeCaptureMaxCursorLagBlocks`: recent block window used when `TradeCaptureSkipStaleCursor=true`; default `2`.
- `PolygonRpcUrl`: fallback Polygon JSON-RPC URL. Do not put secret RPC tokens in repository files.
- `RpcUrlEnvironmentVariable`: environment variable override, default `POLYCOPYTRADER_POLYGON_RPC_URL`.
- `LookbackDays`: fresh catch-up seed window, currently validated between `1` and `30`; default `7`.
- `MaxBlockRange`: `eth_getLogs` block span per request; default `500`; keep it at or below `10000` for public/free RPC endpoints.
- `RequestDelayMilliseconds`: delay between RPC/Gamma calls to avoid hammering public endpoints.
- `TradeCapturePollDelayMilliseconds`: pause between diagnostic latest-block polling cycles; default `250`, set to `0` only when the RPC provider can handle continuous polling.
- `TradeCaptureRequestDelayMilliseconds`: optional delay between diagnostic `eth_getLogs` and block-timestamp RPC calls inside one catch-up cycle; default `0`.
- `TradeCaptureStartLookbackBlocks`: number of recent blocks to scan when no diagnostic cursor exists; default `20`, set to `2` in the low-latency service config.
- `TradeCaptureConfirmations`: blocks to lag behind the latest Polygon head in diagnostic mode; default `0` for lowest latency, with possible reorg artifacts.
- `TradeCaptureErrorDelayMilliseconds`: first retry delay after diagnostic RPC/storage errors; default `1000`.
- `TradeCaptureMaxErrorDelayMilliseconds`: maximum diagnostic exponential retry delay; default `30000`.
- `PaperSignalEnabled`: converts `OrderFilled` captures into Paper-runtime signal evaluations and paper orders when all gates pass; default `false` in code and enabled in the service appsettings used for the current experiment.
- `PaperSignalBacklogEnabled`: enables the older backlog worker that reads unprocessed rows from `polymarket_onchain_trade_captures`; default `true`, disabled in the low-latency service config.
- `PaperSignalHotPathEnabled`: evaluates fresh decoded captures directly inside the trade-capture loop before any optional capture persistence; default `true`.
- `PaperSignalHotMaxAgeSeconds`: maximum age of a decoded capture accepted by the hot path before candidate lookup; default `2`.
- `PaperSignalLatestCandidatesLimit`: maximum number of newest decoded captures considered by the hot Paper selection path per block range; default `100`.
- `PaperSignalPollDelayMilliseconds`: pause between paper-signal cycles; default `250`.
- `PaperSignalBatchSize`: maximum unprocessed maker/taker participants loaded from diagnostic captures per cycle; default `250`.
- `PaperSignalMaxLagSeconds`: maximum age accepted for an on-chain trade signal; default `300`, set to `2` in the low-latency service config.
- `PaperSignalRatingStaleAfterHours`: maximum age for the matched Polymarket wallet/category rating row; default `24`.
- `PaperSignalRequirePolymarketRatingFound`: reject rows where the rating refresh completed but Polymarket did not return the wallet for that category slice; default `true`.
- `PaperSignalMinLeaderboardPnlUsd`: minimum Polymarket leaderboard PnL gate for the copied wallet/category row; default `0`.
- `PaperSignalMinLeaderboardPnlToVolumePct`: minimum derived leaderboard PnL-to-volume efficiency gate; default `0`.
- `BackgroundSyncEnabled`: runs on-chain ingestion continuously while the service is running; default `true`.
- `BackgroundSyncIdleDelaySeconds`: pause between successful background ingestion cycles; default `30`.
- `BackgroundErrorDelaySeconds`: first retry delay after background ingestion or enrichment errors; default `60`.
- `BackgroundMaxErrorDelaySeconds`: maximum exponential retry delay after repeated background errors; default `900`.
- `MarketEnrichmentBatchSize`: number of queued missing on-chain token ids to enrich per Gamma batch; default `100`.
- `MarketEnrichmentMaxBatchesPerRun`: maximum Gamma enrichment batches per manual `Enrich markets` command; default `25`. If this limit is reached while queued due tokens remain, run the command again to continue.
- `BackgroundMarketEnrichmentEnabled`: runs missing-token Gamma enrichment continuously while the service is running; default `true`.
- `MarketEnrichmentIntervalSeconds`: pause between successful background enrichment cycles; default `120`.
- `BackgroundPositionRefreshEnabled`: runs wallet-position aggregation continuously while the service is running; default `true`.
- `PositionRefreshIntervalSeconds`: pause between successful background position refresh cycles; default `60`.
- `PositionRefreshTokenBatchSize`: number of queued token ids to aggregate into wallet positions per cycle; default `25`.
- `PositionRefreshQueueSeedTokenBatchSize`: number of missing token ids to seed into the position refresh queue while the initial positions table is being built; default `100`.
- `BackgroundActivityRefreshEnabled`: runs wallet-activity ranking aggregation continuously while the service is running; default `true`.
- `ActivityRefreshIntervalSeconds`: pause between successful background activity refresh cycles; default `90`.
- `ActivityRefreshWalletBatchSize`: number of queued wallets to aggregate into wallet activity per cycle; default `50`.
- `ActivityRefreshQueueSeedWalletBatchSize`: number of missing wallets to seed into the activity refresh queue while the initial activity table is being built; default `100`.
- `BackgroundPerformanceRefreshEnabled`: runs wallet-performance aggregation continuously while the service is running; default `true`.
- `PerformanceRefreshIntervalSeconds`: pause between successful background performance refresh cycles; default `120`.
- `PerformanceRefreshWalletBatchSize`: number of queued wallets to aggregate into wallet performance per cycle; default `50`.
- `PerformanceRefreshQueueSeedWalletBatchSize`: number of missing wallets to seed into the performance refresh queue while the initial performance table is being built; default `100`.
- `BackgroundCategoryPerformanceRefreshEnabled`: runs wallet-category performance aggregation continuously while the service is running; default `true`.
- `CategoryPerformanceRefreshIntervalSeconds`: pause between successful background wallet-category performance refresh cycles; default `150`.
- `CategoryPerformancePairBatchSize`: number of queued wallet/category pairs to aggregate per cycle; default `250`.
- `CategoryPerformanceQueueSeedPairBatchSize`: number of missing wallet/category pairs to seed into the category performance refresh queue while the initial table is being built; default `250`.
- `BackgroundSignalCandidateRefreshEnabled`: runs the on-chain signal-candidate materialization worker while the service is running; default `true`.
- `SignalCandidateRefreshIntervalSeconds`: pause between successful candidate materialization cycles; default `60`.
- `SignalCandidateBatchSize`: number of queued wallet-fill rows to evaluate into candidate/reason rows per cycle; default `250`.
- `SignalCandidateQueueSeedBatchSize`: number of wallet-fill source rows to advance through the historical candidate backfill cursor per cycle; default `1000`.
- `SignalCandidateRetryBatchSize`: number of temporarily rejected candidates to requeue per cycle when metadata/category/performance may have become available; default `250`.
- `ExchangeContracts`: Polymarket V1/V2 CTF and negative-risk exchange contracts to scan.

Activity, position, wallet-performance, and wallet/category-performance refresh
cycles share a non-blocking PostgreSQL advisory lock. If another derived refresh
cycle is already running, a worker skips its current cycle instead of overlapping
transactions against the same materialized tables. This favors steady throughput
over parallel refresh attempts that can deadlock and roll back.

Signal-candidate materialization is queue based. The historical backfill cursor
walks all downloaded `polymarket_onchain_wallet_fills` once in source order,
queues missing candidates in bounded batches, and ingestion queues newly added
wallet fills as block ranges are decoded. This avoids scanning the whole history
on every one-minute worker cycle.

The older full on-chain collection and derived-data workers are temporarily
paused in the default service configuration: `OnChainIngestion:Enabled` and the
older on-chain background flags are `false`, and those hosted-service
registrations in `PolyCopyTrader.Service/Program.cs` are commented out. The
diagnostic trade-capture worker is registered independently and controlled by
`TradeCaptureEnabled`. Existing PostgreSQL data remains available for analysis.
To resume full background collection/processing, uncomment the older
hosted-service registrations and set the relevant flags back to `true`.

The diagnostic trade-capture worker stores decoded `OrderFilled` rows in
`polymarket_onchain_trade_captures` and stores one cursor per exchange contract
in `polymarket_onchain_trade_capture_cursors`. It does not write
`polymarket_onchain_logs`, `polymarket_onchain_fills`,
`polymarket_onchain_wallet_fills`, or any derived performance/signal tables.
This keeps the experiment isolated so the table can be truncated or dropped if
it grows too quickly.

The paper-signal worker reads the same diagnostic captures and writes its
dedupe/audit results to `polymarket_onchain_paper_signal_results`. The legacy
backlog path still evaluates one maker participant and one taker participant per
persisted fill. The low-latency hot path is stricter: before candidate lookup it
keeps only the newest `PaperSignalLatestCandidatesLimit` decoded captures, then
resolves that window through `polymarket_gamma_markets`,
`polymarket_category_mappings`, and
`polymarket_data_api_wallet_category_ratings`. SELL participants are dropped
from hot trading selection. BUY candidates are pre-scored using cheap fields
such as category, freshness, size, market end time, and Polymarket rating
presence, then attempted in score order until an order is created or a
non-orderbook rejection stops the batch. Accepted BUY signals while Paper runtime
is enabled open or add to copied-wallet paper positions and create a
`paper_copied_leader_positions` link. In the hot path, a candidate's order book
is read from the public market WebSocket cache first; if the in-memory book is
missing, stale, unsubscribed, or unusable, the service immediately fetches CLOB
`/book`, updates the in-memory cache, and evaluates the candidate against that
snapshot. The candidate is rejected with a REST/empty-side `missing_orderbook_*`
reason only when `/book` is unavailable or unusable. Paper/live exposure is read
from an in-memory snapshot cache instead
of three PostgreSQL reads on every selected candidate, and accepted on-chain
Paper BUYs are persisted as one PostgreSQL transaction covering signal, paper
order, copied-leader link, and on-chain result. Direct on-chain SELL
notifications are not copied by the hot path; copied exits are tracked from
leader Data API activity instead. With `PaperTrading:UseMinimumMarketOrderSize=true`,
proposed on-chain BUY paper orders use the market `min_order_size`. The capture
worker logs `FetchMs`, `DecodeMs`, `HotSignalMs`, `PersistMs`, and `TotalMs`;
the hot signal processor logs `CandidateLookupMs`, `SelectionMs`,
`ProcessingMs`, `OrderBookMs`, `ExposureMs`, `EvaluationMs`, `PersistenceMs`,
and `TotalMs` for tuning the candidate limit. This path does not create live
orders.

The leader activity exit worker is controlled by the `PaperTrading`
`LeaderActivityExitTracking*` settings. It selects due active
`paper_copied_leader_positions`, calls Data API `/activity?user=<wallet>` with
`sortBy=TIMESTAMP`, `sortDirection=DESC`, `limit=500`, and a timestamp
cache-buster, then filters `TRADE`/`SELL` rows for the same asset after the
copied entry. Matched rows are stored in
`paper_copied_leader_activity_events` with a dedupe key. The worker creates a
paper SELL order priced at the leader's sell activity price, proportional to the
leader's partial exit, and capped by the available copied-wallet paper position
after already-open SELL orders. Activity rows with invalid prices are skipped.
This path does not create live orders.

The cursor stores a completed block range per contract: `to_block` is extended
forward as new blocks are ingested. `from_block` is kept as the oldest block
already retained for that contract; ingestion no longer moves it backward for
historical backfill. Stopping the run after a completed batch is safe; the next
run resumes from `to_block + 1` and checks only new blocks.

Raw Polygon log rows are stored in `polymarket_onchain_logs` only until their
decoded fill has been materialized into the indexed serving layer. Decoded fills
are stored in `polymarket_onchain_fills` and remain the rebuild/audit source. The
service also derives `polymarket_onchain_wallet_fills` with one maker row and one
taker row per fill, then aggregates those rows into
`polymarket_onchain_wallet_executions` by wallet, transaction hash, token id, and
side. The dashboard ranking uses those executions, so it is no longer maker-only.
Wallet fills are also materialized into `polymarket_onchain_signal_candidates`
and `polymarket_onchain_signal_candidate_reasons`. This layer is read-only
behavior evidence for selecting trusted `(wallet, category)` pairs. It records
BUY and SELL wallet-fill observations with category, market status, and
wallet/category performance snapshots. Current market state is stored for audit
but does not reject historical evidence; rejected rows are limited to missing
data or wallet/category performance that does not pass the configured gates. It
does not place live or paper orders.
If raw fills predate the wallet or serving tables, the next on-chain sync fills
the missing derived range from PostgreSQL before it continues reading new Polygon
blocks.

`polymarket_onchain_trade_details` is an indexed table for the trade-level
explorer. It is incrementally upserted from decoded fills plus token metadata and
exposes block time, transaction hash, maker/taker participants, maker/taker side,
price, share size, notional, raw asset amounts, fees, market title/slug, outcome,
category, and resolved status. `polymarket_onchain_participant_details` is an
indexed table for the participant-level explorer. It is incrementally refreshed
from materialized activity, positions, and performance so each wallet has
executions, buy/sell counts, markets traded, volume, fees, position counts,
exposure, resolved PnL, ROI, win rate, score, and first/last trade time in one
row. Both tables are research surfaces; they do not place orders.

`polymarket_onchain_wallet_activity` is a materialized activity-ranking table
maintained by the background activity refresh worker. It reads wallet executions
by queued wallet and stores execution count, buy/sell execution counts, distinct
token count, notional volume, average trade size, collateral-denominated fees,
activity score, and first/last trade time. `Onchain Rankings` reads this table
instead of grouping the full execution table during each dashboard refresh.
`polymarket_onchain_wallet_activity_refresh_queue` stores wallets that need
recalculation; derived-data rebuilds enqueue affected wallets, and first startup
after the feature is introduced seeds missing wallets in batches.

`polymarket_onchain_wallet_positions` is a materialized table maintained by the
background position refresh worker. It groups by wallet, token, market, and
outcome, then exposes buy/sell shares, net shares, net cost, average buy/sell
price, volume, first/last trade time, status, and resolved PnL when Gamma
metadata provides a winning outcome. `polymarket_onchain_position_refresh_queue`
stores token ids that need recalculation; ingestion, derived-data rebuilds, and
Gamma enrichment enqueue affected token ids. During first startup after the
feature is introduced, the worker seeds missing token ids in batches until the
existing execution history has a positions row.

`polymarket_onchain_wallet_performance` is a materialized wallet score table
maintained by the background performance refresh worker. It reads the positions
table and stores position counts, open/resolved counts, market count, volume,
open exposure, resolved cost, resolved PnL, resolved ROI, win rate, average
position size, sample quality, and a transparent first-pass score. The score is
heuristic, not a trading command. `polymarket_onchain_wallet_performance_refresh_queue`
stores wallets that need recalculation; position refreshes enqueue affected
wallets, and first startup after the feature is introduced seeds missing wallets
in batches until the existing positions history has a performance row.

`polymarket_onchain_wallet_category_performance` is the category-scoped wallet
score table. It uses the same transparent first-pass score as wallet performance
but groups positions by `(wallet, category)`, with unknown or unenriched
categories stored as `unknown`. `polymarket_onchain_wallet_category_performance_refresh_queue`
stores wallet/category pairs to recalculate. Position refreshes enqueue both the
previous and new category pairs for affected tokens, so category scores stay
current as new fills arrive or Gamma metadata changes.

The manual `Enrich markets` command calls Gamma `markets?clob_token_ids=...` for
queued missing or incomplete execution token metadata and stores
`polymarket_onchain_token_metadata` rows with condition id, market title/slug,
outcome, category, end date, active/closed status, raw JSON, and not-found
markers for tokens Gamma cannot resolve. Ingestion and derived-data rebuilds
write affected token ids to `polymarket_onchain_token_metadata_refresh_queue`,
so enrichment no longer repeatedly scans the full wallet-execution table to find
missing metadata. Rows with failed lookup or blank
category are retried, and category parsing falls back from `market.category` to
nested event/category fields when Gamma omits the top-level category. If token
lookup returns metadata without a category, enrichment fetches the linked Gamma
event and derives a category from event category/tags/text before falling back to
CLOB `markets-by-token/{token_id}` and Gamma `condition_ids`. It rechecks missing token ids after every stored batch and
continues until no queued due tokens are left or `MarketEnrichmentMaxBatchesPerRun` is reached.
The background enrichment worker runs the same processor every
`MarketEnrichmentIntervalSeconds`.

The on-chain background workers record transient failures in `api_errors`, then retry
with exponential backoff from `BackgroundErrorDelaySeconds` to
`BackgroundMaxErrorDelaySeconds`. Single-run guards prevent manual IPC commands
and background workers from running duplicate ingestion, enrichment, activity,
position, performance, or category performance cycles.

The dashboard has two on-chain ranking layers. `Onchain Rankings` is still
activity-based: executions, buy/sell counts, distinct token ids, notional volume,
and maker-side fees where the fee asset is collateral, but it is served from the
materialized activity table. `Onchain Leaders` is the first performance-based
view over materialized positions. It depends on Gamma metadata and resolved
markets for PnL/win-rate signals and does not include current mark-to-market yet.

## Analytics

Controls daily report generation, dashboard report limits, and CSV export directory.

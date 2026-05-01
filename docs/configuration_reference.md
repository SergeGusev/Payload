# Configuration Reference

Configuration lives in `appsettings.json`, environment variables, and secret providers.
Do not commit real credentials.

## Bot

- `Mode`: `ReadOnly`, `Paper`, `DryRun`, or `Live`.
- `PollIntervalSeconds`: main service loop interval.
- `HeartbeatIntervalSeconds`: heartbeat cadence.
- `UseWebSockets`: enables market WebSocket monitoring when true.
- `EnableLiveTrading`: must be true for live trading, but is not sufficient by itself.

## LiveTrading

- `ManualEnableCode`: must equal `LIVE_TRADING_ENABLED` for live trading.
- `MaxOrderNotionalUsd`: hard tiny-size cap per live order.
- `MaxTradeBankrollPct`: live trade bankroll cap.
- `MaxMarketBankrollPct`: live market bankroll cap.
- `MaxDailyLossPct`: live daily loss lockout reference.
- `MaxTotalDeployedPct`: live total deployed cap.
- `DefaultOrderTtlSeconds`: GTD order lifetime, max 300 seconds.
- `MaxClockDriftSeconds`: maximum allowed CLOB server-time drift.
- `ApiErrorLockoutCount`: recent Polymarket error threshold.
- `ApiErrorLockoutWindowMinutes`: lockout lookback window.
- `MaxOpenLiveOrders`: open live order cap.
- `CancelAllOnKillSwitch`: documents intended kill-switch behavior.

## Polymarket

- `DataApiBaseUrl`: public Data API base URL.
- `ClobBaseUrl`: public/authenticated CLOB API base URL.
- `GammaBaseUrl`: Gamma API base URL, reserved for future enrichment.
- `GeoblockUrl`: geoblock check URL.
- `TimeoutSeconds`: outbound HTTP timeout.
- `MaxRetries`: retry count for transient public API failures.
- `RetryBaseDelayMilliseconds`: base retry delay.
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

## PolymarketAuth

Only lookup names belong in config. Secret values belong in environment variables or
Windows Credential Manager.

- `Enabled`: enables auth readiness checks.
- `SecretProvider`: `Environment` or `CredentialManager`.
- `SigningAddress`: wallet that signs EIP-712 messages.
- `FunderAddress`: funded Polymarket wallet/proxy used as maker.
- `ChainId`: Polygon is `137`.
- `SignatureType`: `EOA`, `POLY_PROXY`, `POLY_GNOSIS_SAFE`, or `POLY_1271`.
- `DryRunSigningEnabled`: enables dry-run signing if the dry-run key exists.
- `DryRunPrivateKeyName`: lookup name for dry-run key.
- `OrderSigningPrivateKeyName`: lookup name for live order signing key.
- `ApiKeyOwnerName`: lookup name for API key owner UUID.
- `ApiKeyName`: lookup name for API key.
- `ApiSecretName`: lookup name for API secret.
- `ApiPassphraseName`: lookup name for API passphrase.

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

Risk settings cap paper and signal sizing. Live trading also applies `LiveTrading`
caps before submitting orders.

## Watchlist

Each trader rule controls wallet, categories, lag, spread, slippage, leader trade size,
and whether the trader is enabled.

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

## OnChainIngestion

Reads Polymarket `OrderFilled` events from Polygon through JSON-RPC. This is a
background research workflow, not trading logic. The dashboard `Onchain sync`,
`Enrich markets`, and `Cancel onchain` buttons call the same processors through
localhost IPC for manual forcing and diagnostics. Progress is checkpointed after
every completed block batch.

- `Enabled`: allows on-chain background workers and manual refresh commands when true.
- `PolygonRpcUrl`: fallback Polygon JSON-RPC URL. Do not put secret RPC tokens in repository files.
- `RpcUrlEnvironmentVariable`: environment variable override, default `POLYCOPYTRADER_POLYGON_RPC_URL`.
- `LookbackDays`: fresh catch-up seed window, currently validated between `1` and `30`; default `7`.
- `MaxBlockRange`: `eth_getLogs` block span per request; default `500`; keep it at or below `10000` for public/free RPC endpoints.
- `RequestDelayMilliseconds`: delay between RPC/Gamma calls to avoid hammering public endpoints.
- `BackgroundSyncEnabled`: runs on-chain ingestion continuously while the service is running; default `true`.
- `BackgroundSyncIdleDelaySeconds`: pause between successful background ingestion cycles; default `30`.
- `BackgroundErrorDelaySeconds`: first retry delay after background ingestion or enrichment errors; default `60`.
- `BackgroundMaxErrorDelaySeconds`: maximum exponential retry delay after repeated background errors; default `900`.
- `MarketEnrichmentBatchSize`: number of missing on-chain token ids to enrich per Gamma batch; default `100`.
- `MarketEnrichmentMaxBatchesPerRun`: maximum Gamma enrichment batches per manual `Enrich markets` command; default `25`. If this limit is reached while missing tokens remain, run the command again to continue.
- `BackgroundMarketEnrichmentEnabled`: runs missing-token Gamma enrichment continuously while the service is running; default `true`.
- `MarketEnrichmentIntervalSeconds`: pause between successful background enrichment cycles; default `120`.
- `BackgroundPositionRefreshEnabled`: runs wallet-position aggregation continuously while the service is running; default `true`.
- `PositionRefreshIntervalSeconds`: pause between successful background position refresh cycles; default `30`.
- `PositionRefreshTokenBatchSize`: number of queued token ids to aggregate into wallet positions per cycle; default `50`.
- `PositionRefreshQueueSeedTokenBatchSize`: number of missing token ids to seed into the position refresh queue while the initial positions table is being built; default `500`.
- `BackgroundActivityRefreshEnabled`: runs wallet-activity ranking aggregation continuously while the service is running; default `true`.
- `ActivityRefreshIntervalSeconds`: pause between successful background activity refresh cycles; default `30`.
- `ActivityRefreshWalletBatchSize`: number of queued wallets to aggregate into wallet activity per cycle; default `100`.
- `ActivityRefreshQueueSeedWalletBatchSize`: number of missing wallets to seed into the activity refresh queue while the initial activity table is being built; default `500`.
- `BackgroundPerformanceRefreshEnabled`: runs wallet-performance aggregation continuously while the service is running; default `true`.
- `PerformanceRefreshIntervalSeconds`: pause between successful background performance refresh cycles; default `30`.
- `PerformanceRefreshWalletBatchSize`: number of queued wallets to aggregate into wallet performance per cycle; default `100`.
- `PerformanceRefreshQueueSeedWalletBatchSize`: number of missing wallets to seed into the performance refresh queue while the initial performance table is being built; default `500`.
- `BackgroundCategoryPerformanceRefreshEnabled`: runs wallet-category performance aggregation continuously while the service is running; default `true`.
- `CategoryPerformanceRefreshIntervalSeconds`: pause between successful background wallet-category performance refresh cycles; default `30`.
- `CategoryPerformancePairBatchSize`: number of queued wallet/category pairs to aggregate per cycle; default `500`.
- `CategoryPerformanceQueueSeedPairBatchSize`: number of missing wallet/category pairs to seed into the category performance refresh queue while the initial table is being built; default `1000`.
- `ExchangeContracts`: Polymarket V1/V2 CTF and negative-risk exchange contracts to scan.

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
missing or incomplete execution token metadata and stores
`polymarket_onchain_token_metadata` rows with condition id, market title/slug,
outcome, category, end date, active/closed status, raw JSON, and not-found
markers for tokens Gamma cannot resolve. Rows with failed lookup or blank
category are retried, and category parsing falls back from `market.category` to
nested event/category fields when Gamma omits the top-level category. If token
lookup returns metadata without a category, enrichment fetches the linked Gamma
event and derives a category from event category/tags/text before falling back to
CLOB `markets-by-token/{token_id}` and Gamma `condition_ids`. It rechecks missing token ids after every stored batch and
continues until none are left or `MarketEnrichmentMaxBatchesPerRun` is reached.
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

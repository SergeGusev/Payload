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
  sizing is controlled by each strategy's `Live $` and `Live bal` values. Stored
  `Live bal` is capped at `100.00` per strategy.
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
- `BlockOnGeoblockCheckFailure`: when `true`, a failed geoblock endpoint
  check blocks live placement. When `false`, only a successful geoblock
  response with `blocked=true` blocks live placement; endpoint failures are
  recorded as `GeoblockCheck` warnings.
- `CancelAllOnKillSwitch`: documents intended kill-switch behavior.

## PaperFakFeeBackfill

- `Enabled`: starts the low-priority historical pure-Paper FAK fee worker. The
  typed fallback is `false`; the checked-in service configuration enables the
  approved migration.
- `ApplyEnabled`: permits atomic fee/net updates. It cannot be `true` when
  `Enabled=false`.
- `HistoricalCutoffUtc`: immutable upper bound for candidates. The approved
  migration uses `2026-08-07T22:44:55.219515Z`, the start of the first deployed
  fee-aware service, so current fills are never treated as history.
- `BatchSize`: maximum rows in one short transaction; default `50`, maximum
  `250`.
- `CycleIntervalSeconds`: delay after a non-terminal page or a foreground-queue
  deferral; default `15`.
- `InitialDelaySeconds`: startup grace period before the first scan; default
  `300`.
- `IdleDelaySeconds`: delay after the end of a complete ranked-strategy sweep;
  default `900`. Sweep end is not a claim that deferred or unavailable rows are
  complete.
- `ErrorDelaySeconds` / `MaxErrorDelaySeconds`: initial and capped exponential
  retry delays; defaults `60` and `900`.

The worker accepts only BUY orders with exact execution sources
`btc_updown5m_fak_taker_paper` and
`btc_updown5m_child_mirror_fak_paper`. It excludes Live-shadow, GTD, Maker,
ambiguous, and already-accounted rows. Pending Paper-entry persistence and
market-data side effects receive the first 15-second cycle, then one bounded
historical batch may run before the worker yields to foreground work again. This
bounded alternation preserves a full-cycle foreground head start without
requiring either queue to reach zero, which may never happen under continuous
load. With permanently nonempty foreground queues, the effective historical
cadence is approximately one batch every 30 seconds. Historical gross PnL
remains unchanged; fee and nullable net PnL are stored separately with
`historical-current-paper-model-v1` provenance.

The conditional apply accepts exactly two dependency shapes. `FullChain`
requires the unchanged exact fill/run/zero-size-position/settlement chain. Its
settlement source/time must be either exact `BtcUpDown5mGammaClosedMarket` at
the run settlement time or exact `MarketWebSocket` at or before the run
settlement time; all identity, economic, uniqueness, and accounting guards
remain required. This shape updates fill, run, position, and settlement.
`RunOnlyLegacy` requires exactly one unchanged, settled, economically
self-consistent run and no position or settlement rows. It updates only fill
and run and never creates the absent rows.

At each sweep start, the worker reads the materialized Dashboard lifetime Gross
realized PnL and freezes one stable ranking, highest first. The ranking includes
strategies with historical FAK-source orders and strategies with an eligible
unresolved Settled Paper run, even when they have no historical FAK candidate.
Equal Gross values use strategy ID as the deterministic tie-break. A source
strategy without a materialized Dashboard snapshot falls back to the same
retained run/fill/settlement Gross formula. Exact `LegacyUnknown`, cutoff, BUY,
and two-source eligibility remains in the strategy-bound exact candidate page.
The page first materializes that strategy's exact allowlisted BUY order IDs,
probes their fills through the existing per-order index, then sorts and limits
only the strategy-local candidate keys before loading the full rows. It does not
scan the global chronological `LegacyUnknown` fill index to discover each
strategy. The worker finishes the bounded exact phase for one strategy before
running that strategy's run-level repair/fallback phase and moving to the next.
Ranking affects scheduling only: Gross PnL and the Dashboard aggregate Net PnL
and Net ROI formulas remain unchanged. The exact phase also retains its cutoff,
source allowlist, candidate filters, and financial formula.

The run-level phase covers historical and future `Settled` Paper runs with
positive `stake_usd` and non-null Gross for every strategy; it has no historical
cutoff or execution-source allowlist and never changes Live accounting. It first
repairs an authoritative `Calculated` or `VenueReported` nonnegative Fee by
setting only a missing or inconsistent Net to `Gross - Fee`, preserving the Fee,
status, source, and fee metadata. Only a run still incomplete after the exact
paths can use the approximate fallback. For each bounded transaction, the worker
recomputes the same-strategy lifetime coefficient
`R = SUM(exact fee_usd) / SUM(exact positive stake_usd)` from complete
`Calculated` or `VenueReported` donor runs satisfying `Net = Gross - Fee`.
Ratio-finalized rows never donate. If no valid donor or no positive aggregate
donor stake exists, the target remains unchanged for a later ranked visit.

The fallback stores `Fee = ROUND(stake_usd * R, 8)` and
`Net = Gross - Fee` on the canonical run only. It writes ordinary
`fee_accounting_status=Calculated` with the exact case-sensitive source
`strategy-settled-fee-stake-ratio-v1`; no `Estimated` status, UI label, or
separate coverage is added. A successfully finalized run is terminal: later
donor changes or exact fee availability do not recalculate or replace it.
Related fills, orders, positions, and settlements are not updated, so their
detailed accounting may retain an earlier status or blank Net even after the
run-backed strategy Net and Net ROI become complete. Lock/query deferrals,
transport or programming errors, and service cancellation never create a
financial estimate; compare-and-set leaves a concurrently completed exact run
untouched and retryable operational failures retain their work for retry.

After completed SQL, item-level structural or accounting conflicts advance the
page cursor and remain untouched for reconsideration in a later ranked sweep.
A whole-batch advisory-lock timeout or query cancellation does not advance the
cursor; the same page is retried. Gross ordering and the Dashboard Gross/Net PnL
and Net ROI aggregate formulas are unchanged; the historical cutoff, source
allowlist, and candidate filters remain unchanged for the exact phase.

The dedicated PostgreSQL table `paper_fak_fee_backfill_events` stores only this
worker's structured lifecycle, strategy-ranking, cycle, and failure events.
Rolling file logs remain the fallback when PostgreSQL is unavailable. Database
event retention is deliberately not configurable: the fixed contract deletes
rows with `occurred_at_utc` strictly older than 24 hours every 10 minutes, up to
the 500 oldest rows in one cleanup cycle. This retention remains active even
when the historical migration is disabled. The table contains only events
emitted after this database-event feature is deployed; earlier file logs are not
backfilled. Inspect the retained window with:

```sql
SELECT occurred_at_utc, level, event_type, message,
       worker_instance_id, sweep_id, cycle_id
FROM paper_fak_fee_backfill_events
WHERE occurred_at_utc >= now() - interval '24 hours'
ORDER BY occurred_at_utc DESC, id DESC;
```

## HistoricalGrossNetParity

This independent historical accounting workflow completes Fee and Net for the
same economic contributions that the existing Gross formulas already select.
It does not change Gross PnL, Gross ROI, fills, execution, settlement outcomes,
or the ordinary accounting path for new bets.

- `Enabled`: starts the workflow; checked-in default `true`. There is no
  `ApplyEnabled`, approval digest, runtime artifact gate, or complete database
  plan. The deployed service processes old targets and applies each
  conflict-free decision immediately.
- `HistoricalCutoffUtc`: fixed at `2026-08-10T00:00:00Z`. Eligibility follows
  the proved originating entry, not a later SELL, settlement, or mark. Mixed or
  unproved Paper lineage defers that target without blocking independent work.
- `BatchSize`: maximum historical targets in one bounded page; default `50`,
  maximum `250`.
- `CycleIntervalSeconds`, `InitialDelaySeconds`, and `IdleDelaySeconds`: normal
  turn, startup, and completed-sweep delays; defaults `15`, `300`, and `900`.
- `ErrorDelaySeconds` / `MaxErrorDelaySeconds`: initial and capped operational
  retry delays; defaults `60` and `900`.
- `CommandTimeoutSeconds`, `LockTimeoutMilliseconds`, and
  `LookupTimeoutSeconds`: bounded PostgreSQL/CLOB operations; defaults `10`,
  `250`, and `10`.
- `LookupMaxAttempts`: fixed at three distinct uninterrupted worker cycles for
  retryable historical market lookup. A service restart resets the in-memory
  attempt count. Direct fixed fallback performs no external lookup.
- `CalculationVersion`: fixed `historical-gross-net-parity-v1`.

The service selects the unfinished strategy with the greatest current Dashboard
Gross and keeps it active while completing both its exact/authoritative/local-
calculation pass and its direct-fixed-fallback pass in bounded pages. Only after
that strategy is complete does the service rerank the unfinished strategies by
current Gross and select the next one. The old
`PaperFakFeeBackfill` switch, cutoff, and apply gate remain independent.

The fallback pass applies `Fee = ROUND_AWAY_8(B * R)` and
`Net = Gross - Fee`, where `B` is the unchanged source-specific Gross ROI basis
and `R` is fixed at `0.0333`. This is equivalent to
`Net ROI = Gross ROI - 3.33` percentage points. The terminal source and policy
contract are versioned and stored as ordinary `Calculated`; no visible
Estimated status is introduced. New fallback decisions do not construct donor
candidates, load donor previews, or revalidate donor selection. The serializable
target transaction still revalidates cutoff, stable tuple, lineage, component,
Gross, basis, CAS, audit, reconciliation, and Live balance ordering. Existing
completed exact, donor, and fixed decisions are not rewritten, and legacy donor
evidence remains readable.

The selected canonical sources mirror the current projection branches:
Settled runs when runs are authoritative, positive open Paper positions,
runless settlement/SELL fallback, and counted settled Live orders. Excluded
Gross rows are excluded from Net requirements and cannot block a strategy. Live
uses already-associated `VenueReported` evidence first, then exact local CLOB
calculation, then direct fixed fallback; this workflow does not add an on-chain
fee matcher. Live accounting first persists immutable baseline, Pending
ownership, canonical accounting, audit, and reconciliation without changing
balance. A separate transaction applies the earliest unfinished initial balance
effect for each strategy in settlement/UUID order, rebases on the current locked
balance, and marks it Completed. An earlier deferred row gates only later initial
balance effects for that strategy. Strictly newer Venue evidence for a Completed
row uses the existing cumulative-delta path. None of these operations changes
`live_stakes`, loss counters, pause state, or notifications.

Canonical state plus permanent financial audit provide restart/idempotency;
restart rescans unresolved and Pending rows rather than resuming a global plan.
The audit is not subject to the 24-hour operational-event cleanup. Rolling and
operational events remain diagnostic.

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

## PolymarketAutoRedeem

Claims resolved winning Polymarket positions from the always-on service. It
fetches redeemable Data API positions, builds standard binary CTF
`redeemPositions(address,bytes32,bytes32,uint256[])` calldata, records the
attempt in `polymarket_auto_redeem_attempts`, and can submit Deposit Wallet
`WALLET` batches through the Polymarket relayer.

- `Enabled`: runs the background auto-redeem worker when true.
- `DryRun`: records claim-ready attempts without submitting.
- `AutoSubmitEnabled`: live relayer submission gate. Live submit currently
  supports `WalletType=WALLET` only.
- `ManualEnableCode`: must equal `AUTO_REDEEM_ENABLED` before live submit can be
  configured.
- `WalletAddress`: wallet/proxy address used for Data API position lookup. If
  empty, the service falls back to `PolymarketAuth:FunderAddress`.
- `ProxyWalletAddress`: optional explicit relayer proxy wallet address for
  recorded attempts.
- `RelayerBaseUrl` and relayer secret-name fields: relayer endpoint and secret
  references. Do not place secret values in appsettings.
- `WalletType`: `WALLET`, `SAFE`, or `PROXY`; auto-submit is currently limited
  to `WALLET`.
- `RelayerSubmissionDeadlineSeconds`: Deposit Wallet batch signature deadline.
- `CurrentPositionsLimit`, `MaxPositionPages`, `MaxClaimsPerCycle`,
  `MaxLiveSubmissionsPerCycle`, and `MinRedeemableValueUsd`: paging and
  throttling limits. Live relayer submission defaults to one claim per cycle so
  the Deposit Wallet nonce is not reused while an earlier action is still
  active.
- `ConditionalTokensAddress`, `CollateralTokenAddress`, `ParentCollectionId`:
  CTF target and calldata constants. The default collateral is pUSD and parent
  collection is the zero bytes32 value.

Negative-risk positions are intentionally recorded as `SkippedUnsupported`
until the adapter-specific redeem path is implemented.

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

The service config keeps this worker disabled by default. When enabled, the
service stores event-level lag diagnostics in
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
- `AssetSymbols`: base assets tracked against USDT, default `ETH`, `SOL`.
- `StaleAfterSeconds`: maximum latest-trade age accepted by the archive worker,
  default `5`.
- `ReconnectBaseDelaySeconds`: initial reconnect delay after stream failure,
  default `2`.
- `ReconnectMaxDelaySeconds`: maximum reconnect delay after repeated stream
  failures, default `60`.
- `ReceiveBufferBytes`: WebSocket receive buffer size, default `16384`.

## OkxExpiryFuturesReference

The service polls public OKX market data for the live linear USD fixed-expiry
contracts and USD index prices used by the BTC/ETH/SOL Futures Basis Premarket
strategies. Each decision requires the three nearest distinct eligible
expiries; the nearest supplies the threshold signal and the following two
confirm only its strict basis sign. It never places OKX orders, degrades to
fewer than three contracts, or substitutes a perpetual contract.

- `Enabled`: runs the futures reference poller when true; default `true`.
- `RestBaseUrl`: OKX REST base URL, default `https://www.okx.com`.
- `AssetSymbols`: fixed-expiry/index assets, default `BTC`, `ETH`, `SOL`.
- `PollIntervalMilliseconds`: per-cycle poll interval, default `1000`.
- `InstrumentRefreshIntervalSeconds`: cadence for refreshing the live
  fixed-expiry instrument catalog, default `300`.
- `RequestTimeoutMilliseconds`: HTTP timeout for each public market-data request,
  default `2000`.
- `StaleAfterSeconds`: maximum accepted futures or index quote age, default `5`.
- `UserAgent`: HTTP User-Agent header sent to OKX.

## CryptoReferencePriceHistory

Stores a clean market-independent BTC/ETH/SOL reference-price history in
`crypto_reference_price_ticks` and maintains fast in-memory rolling averages for
strategy use. It reads current prices from the existing Binance BTC and crypto
trade-stream services, not from Polymarket odds tables.

- `Enabled`: runs the reference-price history worker when true; default `true`.
- `AssetSymbols`: assets persisted and averaged, default `BTC`, `ETH`, `SOL`.
- `WriteIntervalSeconds`: write cadence and minimum averaging step, default
  `10`.
- `StartupLookbackHours`: history loaded from PostgreSQL into memory at service
  start, default `24`.
- `TargetSamplesPerWindow`: target number of downsampled buckets per averaging
  window, default `60`.
- `WindowMinutes`: rolling-average windows in minutes, default `1440`, `720`,
  `360`, `180`, `90`, `45`, `20`, `10`.

The default proportional steps are therefore `24h=1440s`, `12h=720s`,
`6h=360s`, `3h=180s`, `90m=90s`, `45m=45s`, `20m=20s`, and `10m=10s`. Each
window average is computed over bucket averages in memory; after startup the
worker updates only the affected bucket and trims expired buckets.

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

Configures the disabled read-only BTC 5m statistics research worker. It polls
the current BTC price during active BTC 5-minute markets, estimates Up/Down
probability from `btc_5m_history` with four-point interpolation, stores decision
ticks in `btc_up_down_5m_statistics_ticks`, and queues live observations for
later application to `btc_5m_history` after the market result is known. It does
not place Paper, dry-run, or live orders.

- `Enabled`: runs the statistics worker when true; default `false`. Keep it
  disabled during normal live operation to avoid writing research-only ticks.
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

## CryptoUpDown5mResultPolling

Runs the BTC/ETH/SOL 5-minute result collector and latency diagnostic. Every
cycle selects local Gamma markets whose 5-minute window has ended. When
provisional order-book results are enabled, the worker first tries to infer the
winner from fresh WebSocket/CLOB `/book` top-of-book evidence and writes a
ledger row with source `TerminalOrderBook`. It then polls the closed-market
Gamma lookup for that concrete slug and writes a confirming or correcting ledger
row with source `GammaClosedMarket` once Gamma returns an unambiguous winner.
The same cycle also upserts one row per market into
`crypto_up_down_5m_result_polling_observations`. The observation row records
poll attempts, first `closed` observation time, first unambiguous Up/Down winner
time, `winning_outcome`, and delay seconds from `market_end_utc`.

- `Enabled`: runs the result polling statistics worker when true; default
  `true`.
- `AssetSymbols`: Up/Down base symbols to track, default `BTC`, `ETH`, `SOL`.
- `PollIntervalSeconds`: interval between polling cycles, default `5`.
- `MaxMarketsPerCycle`: maximum local Gamma markets scanned per cycle, default
  `500`.
- `MaxMarketAgeMinutes`: maximum age since 5-minute market end for selecting a
  candidate, default `60`.
- `MaxResultWaitMinutes`: maximum time after market end before a pending row is
  marked `TimedOut`, default `20`.
- `ReferencePriceResultEnabled`: when true, infers BTC/ETH/SOL 5-minute
  outcomes from archived Binance start/end reference ticks before waiting for
  Gamma, default `true`.
- `ReferencePriceResultMaxEndAgeMilliseconds`: maximum age of the latest
  archived tick before market close for reference-price result inference,
  default `15000`.
- `ReferencePriceResultMinSamples`: minimum archived ticks required before
  comparing start and end reference prices, default `2`.
- `ProvisionalOrderBookResultEnabled`: when true, writes provisional result
  ledger rows from terminal order-book evidence before Gamma confirmation,
  default `true`.
- `ProvisionalWinnerBidMin`: minimum best bid for the inferred winning outcome,
  default `0.60`.
- `ProvisionalLoserAskMax`: maximum accepted opposite-outcome top-book evidence
  for provisional inference, default `0.40`.
- `ProvisionalMaxOrderBookAgeMilliseconds`: maximum WebSocket cache age for
  provisional inference before REST fallback is considered, default `15000`.
- `ProvisionalRestFallbackEnabled`: when true, uses CLOB `/book` for provisional
  inference if cached order-book depth is missing or stale, default `true`.
- `ProvisionalRestRequestTimeoutSeconds`: CLOB `/book` timeout for provisional
  fallback requests, default `3`.

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
- `MaxPoolSize`: optional Npgsql connection-pool ceiling. The service config uses `64`; the Dashboard config uses `8`.
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
winner: realized live PnL is added for wins and subtracted for losses, then the
stored value is clamped to the `0.00` to `100.00` range. Paper trading does not
use this balance.

Live order response bodies are stored in PostgreSQL `jsonb`. Plain-text CLOB
error bodies, such as temporary service-unavailable messages, are wrapped before
storage so they are still persisted without causing a JSON cast failure.
Live FAK accounting uses exact CLOB order-level fill data when available.
Aggregate Polymarket Data API positions are recorded only as observations and do
not update per-order filled size, average fill price, Paper-shadow fills, or
realized Live PnL.
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
- `CopiedTraderPerformanceProjectionEnabled`: enables the incremental local copied-trader Paper performance worker; default `true`. Order, fill, position, and settlement changes enqueue only the affected copied wallets. Multi-wallet Paper mutations acquire deterministic transaction-scoped wallet locks before position, queue, or copied-leader-position locks; entry and settlement then write positions before queue-triggering rows. This prevents mark-versus-settlement, mixed insert/update trigger, and copied-leader exit lock inversions. Selected pending rows are moved in a short `READ COMMITTED` transaction to a durable in-flight table before aggregation, so producers can enqueue a fresh row for the same wallet without waiting for the long projection. Interrupted in-flight work is recovered before new pending work. A separate session advisory lock keeps the two-phase projection cycle single-owner across service instances. The derived aggregate reads only open `paper_positions` rows with `size_shares > 0`, supported by the wallet-leading partial index `ix_paper_positions_open_wallet`; settled-position counts, outcomes, and settlement PnL remain sourced from `paper_position_settlements`, while realized sell-fill PnL remains sourced from `paper_fills`. Closed zero-size Paper positions and all Live or Live-shadow history are not deleted or compacted by this optimization. A low-priority lexical reconciliation sweep also revisits historical wallets so category metadata changes and missed events are repaired without a full-table rebuild in the settlement path.
- `CopiedTraderPerformanceRefreshSeconds`: fixed cadence between projection starts; default `30`. The first cycle runs immediately, and later starts stay anchored to the worker cadence instead of adding the prior SQL duration to the interval. The projection stores an `OVERALL` row and category rows per copied wallet. Its score is a bounded 0-100 local rating based on our Paper PnL, ROI, win rate, settled sample size, lost positions, and open-position penalty.
- `CopiedTraderPerformanceWalletBatchSize`: reserved maximum high-priority dirty wallets recomputed in one projection transaction; default `25`, allowed range `1` to `250`.
- `CopiedTraderPerformanceReconciliationWalletBatchSize`: separately reserved maximum low-priority reconciliation wallets recomputed in the same cycle; default `5`, allowed range `1` to `250`. Durable in-flight recovery consumes its original class budget first. Unused high-priority and reconciliation capacity does not spill into the other class, keeping the configured database-load and freshness contract deterministic; the default hard limit is therefore `30` wallets per cycle.
- `CopiedTraderPerformanceReconciliationSeedWalletBatchSize`: upper bound for historical wallets considered by each lexical reconciliation step; default `100`, allowed range `1` to `1000`. The effective seed is additionally limited to reconciliation slots left after draining existing low-priority backlog, and seeded wallets are processed in the same transaction. Reconciliation therefore cannot grow its queue faster than its reserved processing budget.

The copied-performance remaining-depth fields count pending plus durable in-flight work. Before downgrading to a build that predates `paper_copied_trader_performance_refresh_inflight`, drain or transactionally requeue every in-flight row; an older worker cannot recover that table by itself.
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
subscription cache and writes BTC/ETH/SOL 5-minute Up/Down results to
`crypto_up_down_5m_websocket_resolved_markets`. Every raw WebSocket
`market_resolved` event is also appended to
`market_resolved_event_diagnostics` with the source component, raw asset ids,
snapshot-match status, recorder action, and raw JSON. A completed Gamma full
scan removes assets that no longer appear in the `active=true&closed=false`
result set.
The dedicated BTC/ETH/SOL 5-minute critical shard additionally records every
raw protocol frame into `market_websocket_frame_diagnostics`, including ping/pong
frames, invalid JSON, parsed event types, asset ids, market ids, resolved-text
flags, parser status, payload hash, and raw payload truncated to 64KB. This is a
diagnostic path only; it does not change strategy entry or settlement behavior.

- `SubscriptionScope`: semantic market filter for Gamma-discovered WebSocket assets. `AllActiveMarkets` preserves broad active-market monitoring; `BtcUpDown5mOnly` registers only BTC Up/Down 5m markets; `CryptoUpDown5mOnly` registers BTC/ETH/SOL Up/Down 5m markets while still keeping pinned/open order/open position assets subscribed separately.
- `MaxSubscribedAssets`: maximum local subscription count; `0` means unlimited. Prefer `SubscriptionScope` for strategy-specific narrowing because a numeric cap can arbitrarily exclude required BTC/ETH/SOL assets.
- `SubscriptionRefreshSeconds`: fallback refresh cadence. New active Gamma assets also signal the WebSocket loop immediately.
- `SubscriptionBatchSize`: number of asset ids per WebSocket subscribe/unsubscribe payload; default `1000`.
- `ShardMaxAssets`: target maximum asset ids per market WebSocket shard; default `3000`.
- `MaxShardConnections`: soft cap for shard connection count; default `64`, `0` means unlimited.
- `ReconnectBaseDelaySeconds`: first reconnect delay and the delay restored after the first parsed market update is accepted by the side-effect queue (`Enqueued` or `Coalesced`); default `2`.
- `ReconnectMaxDelaySeconds`: cap for exponential reconnect delay across repeated connect/close flaps without an accepted market update; default `60`. Malformed JSON, `PING`/`PONG`, zero-update payloads, rejected/dropped updates, failed dispatch, and cancellation do not reset the delay.
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
restarting all shards on every page. Active BTC/ETH/SOL 5-minute Up/Down
assets are isolated into
`PolymarketMarketWebSocket:crypto-updown-5m-critical` before operational
assets are allocated, so Diff result capture does not depend on the much larger
position/order/signal subscription set. Raw frames from this critical component
are persisted to `market_websocket_frame_diagnostics` for event-type delivery
auditing. The supervisor stores the aggregate
status in `market_data_status.component='PolymarketMarketWebSocket'` and stores
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
markets for the crypto Binance and Diff-family variants, and records one lifecycle row per
market and strategy variant in `strategy_market_paper_runs`. Built-in BTC variants
no longer include standard `Less`/`More` or Gamma comparison rows; the remaining rows include `Middle N`
for `N=100,90,80,...,10`, threshold `Middle N 1..100 bps` and matching
`Instant` variants, fixed `Up 1..50 bps Instant` and
`Down 1..50 bps Instant` variants, `Binance`, threshold `Binance 1..50 bps` in 1 bps increments, matching `Binance 1..50 bps Instant` variants, fixed-price `Binance 45/47/49`, delayed
`Binance 15s/30s/45s`, `Binance Clever`, fair-value `Binance Edge 2/4/6`,
`Prev Score Countertrend 10..90`, singular immediate ask-depth `Prev Score Countertrend`, `Ensemble 2 of 3`, `Dynamic Markov`, `Strategy Selector`, Diff `Up/Down N Instant` thresholds `1..10` in steps of 1 and `15..150` in steps of 5, AdjustedDiff `Up/Down N Instant` thresholds `1..10`, `15`, and `20`, and ShiftDiff `Up/Down S N Instant` rows for shift `1..6` and thresholds `1..12`. ETH/SOL Binance bps rows have been removed from the seed set and local/server history. ETH/SOL variants also include
fixed `Up 1..50 bps Instant` / `Down 1..50 bps Instant` rows, Diff
`Up/Down N Instant` rows with thresholds `1..10` in steps of 1 and `15..150` in steps of 5, AdjustedDiff `Up/Down N Instant` rows, and ShiftDiff `Up/Down S N Instant` rows. More and Revert variants have been removed from the seed set and local/server history. The deleted comparison selector logic is retained only for old diagnostics/tests: when
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
`PaperTakerMaxReferenceSlippage` instead of skipping immediately. Historical capped `Less ... Below ...` and `More ... Below ...` variants kept
the standard CLOB-first selector at their respective delays and placed
a GTD limit BUY at the configured `Below` cap instead of requiring
immediate executable liquidity below that cap. The historical
`Gamma`-suffixed variants intentionally use the older
Gamma-first selector for comparison: `Less Gamma` selects the lower Gamma
outcome price, `More Gamma` selects the higher Gamma outcome price, and the
selected asset then uses the CLOB/WebSocket quote as the GTD limit seed, or the
same resting-limit empty-ask fallback when the selected book has no asks. Historical
`More ... Gamma Below ...` variants kept that Gamma-first selection but placed
a GTD limit BUY at the configured cap. Gamma comparison strategy rows are no longer seeded for live
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

Diff Instant variants use the fast Diff worker. The service keeps in-memory BTC/ETH/SOL
counters for the current UTC day, resets them to zero at `00:00 UTC`, and
updates them from accepted result-ledger rows in the existing
`crypto_up_down_5m_websocket_resolved_markets` table. Accepted sources are
`MarketWebSocket`, `TerminalOrderBook`, and `GammaClosedMarket`. Parent `Up N
Diff Instant` rows use `UpCount - DownCount`; when that value is at least `N`,
they BUY FAK `Down` from current executable ask depth. Parent `Down N Diff
Instant` rows use `DownCount - UpCount`; when that value is at least `N`, they
BUY FAK `Up`. Diff thresholds are `1..10` in steps of 1 and `15..150` in steps
of 5. Revert Diff rows have been removed and are not seeded.

AdjustedDiff Instant variants are parallel copies. They use the same
accepted result rows, but keep a separate continuous in-memory counter that does
not reset at `00:00 UTC`. They compute a slow trend zero from an EMA of raw
`Diff = UpCount - DownCount` (`24` points, `12` point warmup, `0.5` max step,
`1` deadband), then compare thresholds against `AdjustedDiff = raw Diff -
trend_zero` for Up-Diff groups and the opposite value for Down-Diff groups.
AdjustedDiff thresholds are capped at `20`: `1..10`, `15`, and `20`. Revert
AdjustedDiff rows have been removed and are not seeded.

ShiftDiff Instant variants are copies with per-strategy continuous
counters. They apply their configured shift value before comparing the trigger
side to thresholds `1..12`; shift values are `1..6`. Revert ShiftDiff rows have
been removed and are not seeded.

All Diff-family variants use the dedicated fast Diff worker. A strategy for market `T`
requires the previous 5-minute market `T-5m` to have an accepted result row; if
that row is missing long enough, the run skips with
`diff_counter_previous_market_resolved_event_missing` and creates no Paper
order. The first `00:00 UTC` raw Diff market starts from zero and does not
require the previous day's `23:55 UTC` result. They use the same Instant FAK
execution and sizing path as bps Instant variants. `DiffCounterInstantMaxPrice` defaults
to `1.00`, so current Diff-family entries are effectively uncapped by the old
`0.50` price limit and take current executable ask depth immediately instead of
placing resting BUYs at `0.50`. Raw Diff
counters are not restored from PostgreSQL, but each fast cycle writes compact
BTC/ETH/SOL snapshots to `crypto_up_down_5m_diff_snapshots` with `up_count`,
`down_count`, `diff`, `market_start_utc`, `sampled_at_utc`, and high-water
metadata so daily Diff charts can be generated later. Diff/AdjustedDiff/ShiftDiff
rows can enter the Paper/Live-shadow order path when
their Dashboard `Live` flag is enabled and all live gates pass. Dashboard categories split them into
`Diff Up`/`Diff Down`, `AdjustedDiff Up`/`AdjustedDiff Down`, and ShiftDiff-by-shift categories per asset.
Diff Shift Progress rows keep their own persistent counters and Sum in
`crypto_up_down_5m_diff_shift_progress_states`. The retained `N Diff Shift Progress
Premarket` rows are BTC `N=3`, ETH `N=1,2,3,5`, and SOL `N=1..5`; they have separate BTC/ETH/SOL Dashboard categories, run 30 seconds before open, synthesize the latest market result
from the reference price, buy Down for positive raw Diff and Up for negative raw
Diff, skip Diff 0, and use `Unit * abs(Diff)` FAK sizing while damping Diff back
to zero after `abs(Diff)` reaches `N`.

Diff Limit Progress Premarket rows also keep persistent `UpCount`, `DownCount`,
and `Sum` in `crypto_up_down_5m_diff_shift_progress_states`, but the counter is
scoped to the UTC day and resets at `00:00 UTC`. ETH and SOL seed `N=1..5`
in separate Dashboard categories; the BTC rows were retired. The worker runs 30 seconds before open, uses resolved ledger results
for older markets, synthesizes the latest previous market from the current
reference price, buys Down when `Diff = UpCount - DownCount` is positive, buys
Up when it is negative, skips Diff 0, and sizes BUY FAK entries as
`Unit * min(abs(Diff), N)`. Diff itself can grow past `N`; only the stake
multiplier is capped.

Diff Real Limit Progress Premarket rows are clones with a saturated real Diff
counter. BTC, ETH, and SOL each seed `N=1..5` in separate Dashboard categories. The worker uses the same
premarket timing, persistence, direction rule, and BUY FAK sizing, but
`UpCount` does not increase when `Diff` is already `N` and the next result is
Up, and `DownCount` does not increase when `Diff` is already `-N` and the next
result is Down. Opposite results still move Diff back inside `[-N, N]`.

The active non-Instant `Middle` variants use opening-limit pricing rather than
taker pricing. At market open `Middle N` reads the latest Binance BTC/USDT
trade-stream price and compares it to the arithmetic mean of the latest `N`
sampled Binance reference prices. The seeded N values are `100,90,80,...,10`;
a strategy skips with `btc_reference_samples_insufficient` until at least `N`
samples are available. If the latest trade is above the N-sample mean, standard
Middle buys `Down`; if it is below, it buys `Up`; equality skips the run.
`Middle N Revert` inspects the same reference value and inverts that final
decision: above mean buys `Up`, and below mean buys `Down`. The old `Middle 2`
through `Middle 5` depths, including their bps and revert-bps variants, are no
longer seeded as active strategies; existing rows are retired by schema
initialization. The `Middle N 1..100 bps` rows keep the same direction logic but
skip unless the absolute latest-trade deviation from the N-sample mean reaches
the configured threshold; otherwise the run skips with
`btc_reference_mean_deviation_below_threshold`. Matching `Instant` Middle bps
rows keep the same signal and threshold gate, then submit BUY FAK taker entries
from executable ask depth using the same instant sizing path and
`InstantOpeningLimitMaxPrice` cap as Binance instant variants. Previous-result bps logic inspects the exact immediately previous 5-minute windows without gaps, but infers those results from close-book
CLOB price evidence instead of waiting for Gamma settlement. The worker captures
`/book` snapshots for active BTC strategy markets and ETH/SOL 5-minute or 15-minute markets during the final
`CloseBookCaptureLookbackSeconds` seconds before close, throttled by
`CloseBookCaptureIntervalSeconds`, and can use the latest stored snapshot for a
token if the book stops responding after close. A full `Up` midpoint still maps
`>= 0.5` to `Up` and `< 0.5` to `Down`, but one-sided evidence is also accepted:
`Up best_bid >= 0.5` means `Up`, `Up best_ask < 0.5` means `Down`,
`Down best_ask <= 0.5` means `Up`, and `Down best_bid > 0.5` means `Down`.
Conflicting one-sided signals skip with `btc_close_book_inference_conflict`; no
usable current or stored book skips with close-book diagnostics. The previous-result bps calculation also records one
`btc_up_down_5m_result_streak_diagnostics` row per target market. Use
`close_book_streak_result_count` to find the longest same-outcome run and
`cumulative_abs_move_bps` to find the maximum accumulated BTC move over the run.
`BTC Up or Down 5m Up 1..50 bps Instant` and `BTC Up or Down 5m Down 1..50 bps
Instant` reuse that same previous-result streak and cumulative BTC move gate,
but keep only one fixed countertrend side. The former `BTC Up or Down 15m
Up/Down 1..50 bps Instant` rows were removed from production and are no longer
seeded because current 15-minute liquidity/volume is too thin for Live use. `Up` enters only when the previous-result bps
countertrend decision is `Up` after a `Down` streak; `Down` enters only when the
countertrend decision is `Down` after an `Up` streak. The opposite side skips
with `btc_previous_market_move_fixed_outcome_mismatch`, and accepted entries use
the executable ask-depth FAK path with an effective max BUY price of
`1.00`, so `InstantOpeningLimitMaxPrice` no longer blocks fixed Up/Down bps
entries.
`ETH/SOL Up 1..50 bps Instant` and `ETH/SOL Down
1..50 bps Instant` reuse that same crypto streak/move gate but enter only when
the previous-result bps countertrend decision matches the fixed side; the opposite side
skips with `btc_previous_market_move_fixed_outcome_mismatch`. The former ETH/SOL
15-minute fixed Up/Down bps Instant rows were removed from production and are no
longer seeded. Seeded ETH/SOL fixed rows can enter the
Paper/Live-shadow path when their Dashboard `Live` flag is enabled and normal
live gates pass.
Skip strategy rows have been removed from the seed set.
The remaining historical opening-limit rows create pending Paper BUY orders as
ordinary GTD limit orders. Their limit
price is dynamic by default: the worker reads recent settled runs for the same
strategy, computes `wins / settledRuns`, subtracts
`OpeningLimitBreakEvenMargin`, caps the result at `OpeningLimitMaxPrice`
(`0.50` by default), and floors it to `OpeningLimitPriceTickSize`. If there are
fewer than `OpeningLimitBreakEvenMinSettledRuns` settled rows, the worker first
bootstraps from the selected outcome order book: `best_ask` at or below `0.50`
is used directly; otherwise `best_bid + tick` is used with a `0.50` cap. If the
book does not contain a usable price, or the resulting limit is not positive,
the run is skipped with explicit diagnostics. Until a new `Middle Revert`
variant has enough own settled rows, it first bootstraps dynamic
pricing from the paired base strategy history by treating base losses as
estimated Revert wins; if that sample is also insufficient, it uses the same
order-book bootstrap. The remaining BTC Maker variant, `BTC Up or Down 5m Up
Maker 50`, uses a maker-style paper decision path and is grouped under `BTC
Up/Down 5m Maker`. After a BTC 5-minute market starts, it baselines the Up
outcome best ask, tracks a fixed high-water best ask in memory, and evaluates
entries only on 30-second slots (`30s` through `270s`, maximum 9 attempts per BTC
5-minute market). On a slot it creates a minimum-size post-only GTD BUY at fixed
`0.50` only when the selected outcome best ask is strictly above `0.50` and
exceeds the previously fixed high-water value. The high-water value is updated only
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
start reaches the configured bps threshold. The matching `Instant` bps variants keep the same signal and bps threshold, but submit BUY FAK taker entries from the selected outcome's current executable ask depth, taking only immediately available liquidity up to the computed order size and cap. `BTC Up or Down 5m Binance 15s`,
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
`0.10` to `0.90` in `0.05` steps. The singular `BTC/ETH/SOL Prev Score
Countertrend` variants keep the same countertrend signal but enter from
immediate executable ask depth instead of a fixed price; ETH/SOL read
`crypto_up_down_5m_odds_ticks` for their own asset. Decision JSON stores the
same score as signed bps (`previous_score_bps = previous_score * 10000`),
absolute bps, and selected signal bps; the Dashboard all-time Strategies grid
also shows average signed score bps, average signal bps, and latest signal bps.
`BTC/ETH/SOL Prev Score Countertrend Premarket` variants enter 30 seconds before the target market
opens and score a synthetic 5.5-minute window ending at that entry time: the
last minute of the market before the currently running market plus the first 4
minutes 30 seconds of the currently running market. The first valid sample in
that synthetic window is used as the score start price; positive score buys
`Down`, negative score buys `Up`, and neutral or insufficient samples skip.
`Prev Score Countertrend Revert` keeps the BTC previous bias direction and uses
the same immediate ask-depth entry model.
The removed `Ensemble 2 of 3` family voted between selected legacy signals and
entered only when at least two available votes agreed on the same single outcome.
`Dynamic Markov` estimates the next
result from recent BTC 5-minute result transitions and enters only when the
conditional next-outcome probability is at least `0.55`. `Strategy Selector`
ranks selected opening-limit strategies by recent positive Paper expectancy and
reuses the best candidate's current signal. These non-Maker variants can record
both sides of the same Polymarket market in Paper; the opposite-outcome guard is
enforced only by Live preflight against open Live BUY orders in the same
condition. The
order size still targets the current market minimum passing size plus a `10%`
safety buffer times the configured Paper stake multiplier. Non-Instant GTD
diagnostics record `post_only=false` plus the selected pricing model inputs, cap,
final limit, expiration mode, local cancel deadline, CLOB wire expiration, and
fallback `OpeningLimitGtdTtlSeconds` (`120` by default). Instant diagnostics
record `order_execution_mode=FAK`, the selected ask-depth VWAP/limit cap, filled
notional, partial-fill state, and zero-fill skips/rejections. When the strategy's
Dashboard `Live` flag is enabled, opening-limit entries can create linked
live-shadow orders through the controlled Paper/Live-shadow path if all normal
live gates pass. Non-Instant orders then use the generic Paper open-order
pipeline for balanced GTD accounting; Instant entries fill immediately from
visible ask depth and cancel any remainder. GTD limit orders that never fill
before expiration are marked `gtd_limit_not_filled` instead of being counted as
won or lost.

PreOpen fixed-direction BTC strategy rows were physically removed from
production and are no longer seeded. Missing PreOpen strategy rows are treated
as deleted/disabled by the strategy-market-run insert guard, so their Dashboard
grouping categories disappear with the deleted strategy rows.

The dashboard `Strategies` tab reads all rows from `strategies` and aggregates
Paper orders, positions, settlements, and strategy run lifecycle counters so
strategy variants can be compared against each other. It also aggregates Live outcome accounting
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
`Big ROI`, `Big settles`, and `Hide progress` filters. `Live only` keeps rows whose manual Live
flag is enabled. `Big ROI` keeps rows with ROI greater than `10` (`Closed ROI` in
`All`, recent `ROI` in the period tabs). `Big settles` keeps rows whose settled
count is greater than `100` (`Settled` positions in `All`, recent `Settles`
runs in the period tabs). `Hide progress` hides rows whose strategy name contains
`Progress`.
Each `Strategies` tab has its own currency filter; selecting BTC, ETH, or SOL
narrows both the visible rows and that tab's category dropdown to categories
present for the selected currency.
The `Enabled` checkbox writes `strategies.enabled` immediately, and the `Paused`
checkbox writes `strategies.paused`. The service refreshes enabled, manual pause,
and auto Live pause state through a short in-memory cache, so disabled strategies
stop creating BTC/ETH/SOL 5-minute entries without a restart, while manually paused strategies stay enabled but skip new Paper and
Live entries with reason `strategy_paused`. Existing Paper positions can still
be settled, and copied leader exits can still be tracked.
The Dashboard `Paper orders` tab loads the first recent orders page; when a
strategy is selected, PostgreSQL applies that strategy filter before the page
limit. The `Live orders` tab uses the same server-side strategy filter but
supports `Prev`/`Next` paging through persisted history in 100-row pages. When
opened from a recent performance period tab (`24 hours`, `6 hours`, or
`1 hour`), both order tabs also pass the same rolling window to storage as
`created_at_utc >= now - window`, and Live paging stays inside that window.

Automatic Live-only strategy pausing has been removed. The Dashboard `Paused`
checkbox remains a manual full Paper+Live pause, and the Dashboard `Live`
checkbox is the persisted live eligibility flag.

- `Dashboard:RefreshIntervalSeconds`: UI refresh timer for the Dashboard; default `60`.
- `Dashboard:StrategyRefreshIntervalSeconds`: minimum interval between Dashboard strategy-performance database refreshes; default `60`. Strategy toggle/stake commands invalidate the cache so command results are shown immediately.
- `Dashboard:StrategiesOnlyMode`: when true, the Dashboard reads service heartbeat plus strategy performance grids and still shows the local `Dashboard Errors` tab for copied/saved refresh, command, export, and strategy edit failures; default `true`. Heartbeat staleness is evaluated against the selected PostgreSQL server clock, not the Dashboard machine clock.
- `Dashboard:OptionalReportTimeoutSeconds`: timeout for optional Dashboard analytics report grids; default `8`. Used only when `Dashboard:StrategiesOnlyMode=false`. If a report times out, the Dashboard keeps the main refresh alive and shows a Diagnostics warning for the skipped report.
- `Dashboard:ProjectionEventBatchSize`: maximum durable projection events applied in one transaction; default `250`, valid range `1..2000`.
- `Dashboard:ProjectionReconciliationIntervalSeconds`: delay between full-strategy reconciliation attempts; default `30`, valid range `5..3600`. Each attempt still rebuilds exactly one strategy, and the repository advisory transaction lock serializes it with other projection work. Reducing the interval therefore increases the sustained database duty cycle without adding reconciliation parallelism.

The strategy grids read flat precomputed rows from `dashboard_strategy_performance_snapshots` and `dashboard_strategy_recent_performance_snapshots`. On first use, the service builds projection state from one repeatable-read PostgreSQL snapshot. Durable outbox triggers then capture changes to strategy state, Paper orders/fills/runs/positions/settlements, and Live orders; high-frequency Paper position marks are coalesced to one pending event per position and compared with a stored position contribution. The projection worker applies those changes continuously and maintains exact `1h`, `6h`, and `24h` expiry through the existing partial index for each active window rather than scanning raw Paper/Live history. An independent worker performs one indexed full-strategy reconciliation after each configured interval with PostgreSQL parallel workers disabled, `work_mem='4MB'`, and a `15s` per-statement timeout. The worker logs the resolved interval on startup; changing it does not alter the one-strategy batch or the fail-closed retention blocker.

- `Enabled`: runs the BTC 5-minute strategy worker when true; default `true`.
- `PollIntervalSeconds`: worker loop delay; default `1` in the service config to reduce BTC entry timing drift.
- `DiffCounterFastPollIntervalMilliseconds`: fixed-rate cadence for the generic, Diff-family, and previous-result due-entry workers; default `500`. Each worker runs its first cycle immediately and then follows a `PeriodicTimer` cadence. A cycle that outlives one or more timer ticks is never overlapped; the missed ticks are coalesced so the next cycle starts immediately instead of adding another full interval. The separate Diff observe worker observes Diff/AdjustedDiff/ShiftDiff markets, while only the Diff due-entry worker processes their due entries and the main BTC strategy worker no longer places them.
- Diff Countertrend uses raw UTC-day counts reset at `00:00 UTC`: after each accepted BTC/ETH/SOL 5-minute result in the current UTC day, the processor updates `UpCount` or `DownCount`, computes `Diff = UpCount - DownCount`, and stores `DiffCount` as a diagnostic cumulative sum of observed Diff values. Strategy thresholds compare against raw `Diff`; `DiffCount` no longer shifts either side of the counter. AdjustedDiff Countertrend keeps a separate continuous in-memory counter, does not reset it at `00:00 UTC`, and compares thresholds against raw `Diff` adjusted by its slow EMA trend zero. ShiftDiff keeps per-strategy continuous counters and applies the configured shift before comparison. Revert variants have been removed. When the immediately previous result is still missing, a Diff-family run stays pending until four minutes after its own market start; only then it is skipped with `diff_counter_previous_market_resolved_event_missing`.
- `DiffCounterInstantMaxPrice`: maximum BUY price cap for Diff/AdjustedDiff/ShiftDiff Instant entries; default `1.00`, which effectively removes the old `0.50` Diff-family cap because valid BUY prices are below `1.00`. Diff-family entries submit BUY FAK taker fills from current executable ask depth and no longer place resting BUYs at `0.50` when current executable ask depth is above half. Fixed Up/Down bps Instant variants also use an effective max BUY price of `1.00`, independent of `InstantOpeningLimitMaxPrice`. Other Instant strategy families continue to use `InstantOpeningLimitMaxPrice` and still skip above their cap with `instant_price_above_max`.
- `ETH Up or Down 5m Down 9 bps`: targeted stats-entry variant that reuses the fixed Down previous-result bps signal from the matching Instant strategy. The ordinary `BTC/ETH/SOL Up or Down 5m Up/Down N bps Reference Average Premarket` and neutral `BTC/ETH/SOL Up or Down 5m N bps Reference Average Premarket` rows use the in-memory crypto reference averages, not the previous market result. They run 30 seconds before open; `N` covers `1..10` in steps of `1`, then `15..100` in steps of `5`. Every configured `24h`, `12h`, `6h`, `3h`, `90m`, `45m`, `20m`, and `10m` `middle` average participates whenever it has at least one valid sample and a positive calculated average. A window is not excluded because `IsFullWindow=false`, its coverage is incomplete, or its history contains gaps. The selector computes `Amax` and `Amin`, preferring the longer window on equal-price ties. An `Up` trigger enters `Down` at least `N` bps above `Amax`; a `Down` trigger enters `Up` at least `N` bps below `Amin`. Neutral rows enter `Down` only above `Amax`, enter `Up` only below `Amin`, and skip inside the envelope or when the corresponding outside move is below the inclusive threshold. Every move uses the oldest available real bucket in the configured `24h` window as its denominator; that window may be incomplete, and no alternate denominator is substituted. Because each valid tick enters every configured nested window, usable shorter-window data also supplies usable `24h` data and its first bucket. If that `24h` start price is nevertheless absent, the decision fails closed with `reference_average_bps_denominator_24h_available_start_price_missing` as an internal configuration/integrity violation. Raw diagnostics use `decision_source=reference_price_average_envelope_bps_premarket_v5`, `algorithm=5`, and `contract=max_min_available_data_envelope_available_24h_start_denominator`; they record `boundary_requires_full_window=false`, `boundary_uses_available_data=true`, `incomplete_data_blocks_decision=false`, both extrema, both moves, selected-boundary completeness, and denominator evidence. ETH Down reference-average rows use the distinct `...down_reference_average_bps...` code/id family, so they do not share the legacy ETH Down previous-result Premarket Dashboard category. The former ETH filtered Down reference-average clones named `ETH Up or Down 5m Down N bps Filtered Average Premarket` for `N=1..10` have been removed and are purged by schema initialization; the current Filtered catalog count is zero, while its shared dispatch retains the available-data policy. Legacy selected ETH previous-result Premarket rows remain at `-10s` for `40..42 bps` and `-5s` for `30..38 bps`; old `-30s` previous-result rows remain catalogued for history/settlement but are disabled by schema initialization. These variants simulate BUY `FAK` taker entry from executable ask depth at worst price `1 - tick`, record fills at ask-depth VWAP, keep only actually filled partial notional, and reject/skip zero-fill cases. Live-shadow submits a BUY `FAK` market amount with `postOnly=false`, no GTD expiration, and no Live Lost counter multiplier. A zero-fill FAK response is stored as a rejected live entry instead of an open order.
- Optimized Average Premarket: `144` base variants: `84` ETH variants (`Up`, `Down`, and neutral over the 28 thresholds `1..10`, then `15..100` step `5`) plus `30` BTC and `30` SOL variants (`Up`, `Down`, and neutral over `1..10`). The ordinary eight-window available-data Max/Min envelope selector and longer-window tie-break run first, followed by the ordinary direction and inclusive threshold checks. Complete and incomplete usable windows participate equally. An otherwise accepted entry is retained only when the direction-relevant selected boundary uses `3h`: `Amax` for an Up trigger, `Amin` for a Down trigger, and the crossed envelope side for neutral. An incomplete usable `3h` can be selected and matched; all other selected windows skip with `optimized_average_required_window_not_selected`. Raw diagnostics include both extrema, selected-boundary completeness, required window, and match flag. The variants are enabled for Paper on first seed, hard-blocked from Live/Live-shadow even if `live_stakes=true`, and excluded from Child parent assignment. The separate ETH `3Hour Average` and `3Hour LowEnter Average` families remain strictly `3h`-only: other windows do not participate, but a usable incomplete `3h` and gaps inside it do not block calculation.
- `BTC/ETH/SOL Up or Down 5m N bps Confirmed Average Premarket`: `84` neutral Bps clones for `N=1..10` and `15..100` step `5`. The base Bps signal must agree with the exact asset-specific Diff Reference Average signal: BTC `M=5`, ETH `M=3`, SOL `M=1`. `BTC/ETH/SOL Up or Down 5m K Diff Confirmed Average Premarket` adds `42` Diff clones for `K=1..10,15,20,25,30`; their base Diff signal must agree with neutral Bps Reference Average BTC `L=45`, ETH `L=5`, SOL `L=35`. Both families run 30 seconds before open, use BUY `FAK` Paper/Live-shadow execution, and store both nested decisions in diagnostics. Their nested Bps Reference Average decision uses the same available-data policy. A missing or rejected confirmation skips, and opposite outcomes skip with `confirmed_average_signal_mismatch`. The exact linked signal logic is evaluated independently of the linked row's runtime flags. First insertion uses `Enabled=true`, `Live=false`, `Paused=false`; `ON CONFLICT` preserves later runtime controls.
- `BTC/ETH/SOL Up or Down 5m N bps Futures Basis Premarket`: futures-basis variants that run 30 seconds before open for `N = 1, 2, 3, 5, 8, 10, 15, 20`. They select the three live OKX linear USD fixed-expiry contracts with the nearest distinct expiries at or after the target market end and compare each best-bid/ask mid with the simultaneous OKX `<asset>-USD` index. The nearest expiry alone must reach the signed `N bps` threshold; the second and third expiries may have any magnitude but must both have the same nonzero basis sign. A zero/opposite confirmation skips with `futures_basis_confirmation_sign_mismatch`, while a nearest basis below threshold retains `futures_basis_move_below_bps_threshold`. Matching Revert variants confirm the raw sign first and invert only the final direction. Missing, invalid, or stale data for any of the three contracts causes an explicit reference-fetch skip; there is no reduced-count or perpetual fallback. Entries use the same BUY `FAK` executable ask-depth path as other Premarket reference strategies, and seeded `Live` is disabled by default.
- `BTC/ETH/SOL Up or Down 5m N Child` and the Child ROI modes: plain Child and Child ROI retain `N=1..24`. After the staged 217-strategy catalog retirement, Child Progress retains only SOL `N=15,17,19,20,21,22,23,24`; Child Progress ROI retains BTC `N=4,5,6,7,8,9,22`, ETH `N=1,20`, and SOL `N=9`. The retained catalog therefore contains 72 Child, 8 Child Progress, 72 Child ROI, and 10 Child Progress ROI rows: 162 total (BTC 55, ETH 50, SOL 57). Parent selection, five-minute refresh, minimum-sample gates, and sample-adjusted ROI formula are unchanged. This service release changes only catalog/seed membership: the 217 stopped database rows and their structured history remain unchanged until the later separate cleanup.
- `StakeUsd`: fallback/default BTC stake multiplier; default `1.00`. When fresh market `min_order_size` is available, BTC Paper and Live stake notional is computed as the minimum passing order notional plus `10%`, multiplied by the strategy's Paper or Live stake value, then rounded up to the next whole USD.
- `EntryGraceSeconds`: maximum late-entry grace after a variant's due time before the run is skipped; default `60`. Previous-result close-book helpers infer each immediately previous BTC 5-minute result from close-book CLOB price evidence. Full `Up` midpoint maps `>= 0.5` to `Up` and `< 0.5` to `Down`; single-sided `Up best_bid >= 0.5`, `Up best_ask < 0.5`, `Down best_ask <= 0.5`, and `Down best_bid > 0.5` are also decisive. If current close-book fetch stops responding, the worker uses the latest stored snapshot for that token when available. Missing or conflicting evidence is skipped with diagnostics in `skip_diagnostics_json`.
- `MaxMarketsPerCycle`: maximum BTC 5-minute Gamma markets observed per cycle; default `500`.
- `MaxEntriesPerCycle`: maximum due entries processed per cycle across variants; default `3000`. Regular due-entry selection expands the final batch boundary to include every run with the same `entry_due_at_utc`, so a single market-open timestamp is not split across cycles. Within the same due timestamp, Live-enabled strategies are processed first and then ordered by a prepared in-memory Live realized PnL snapshot, with the highest Live realized strategies first. Middle reference opening-limit variants also get a shared fast skip pass before generic order placement, skipped runs are batch-updated through one repository call, and accepted BTC Paper signal/order rows are inserted through one transactional repository call.
- `MaxConcurrentEntryDecisions`: maximum due-entry decision work items processed concurrently after the shared fast paths; default `32` in the service config. This is one processor-wide allowance shared by main, fast Diff, previous-result, and pre-open sell-exit flows rather than a separate allowance for each worker.
- `ChildParentRefreshDelaySeconds`: delay after each UTC-aligned five-minute market start before the dedicated Child/Parent assignment refresh; default `60`, valid range `0..240`. The refresh does not run in the main or fast due-entry loops.
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
- `OpeningLimitDynamicBreakEvenPricingEnabled`: when true, historical opening-limit GTD limit prices are derived from each strategy's own recent settled win-rate; default `true`.
- `OpeningLimitBreakEvenLookbackRuns`: maximum settled runs read for the dynamic opening-limit win-rate; default `100`.
- `OpeningLimitBreakEvenMinSettledRuns`: minimum settled runs required before a dynamic historical opening-limit break-even price is trusted; before that, opening orders use the selected outcome order-book bootstrap; default `30`.
- `OpeningLimitBreakEvenMargin`: safety amount subtracted from `wins / settledRuns` before placing historical opening-limit orders; default `0.10`.
- `OpeningLimitMaxPrice`: maximum historical opening-limit BUY price after the break-even margin; default and maximum `0.50`.
- `OpeningLimitPriceTickSize`: tick used to floor dynamic historical opening-limit prices; default `0.01`.
- `OpeningLimitGtdTtlSeconds`: fallback lifetime for BTC opening-limit GTD Paper and Paper/Live-shadow orders when market-relative expiration is disabled or market end is unavailable; default `120`, valid range `30..300`.
- `OpeningLimitExpireBeforeMarketEndSeconds`: local BTC opening-limit GTD cancel deadline offset from market close; default `60`, valid range `0..300`, with `0` disabling the market-relative deadline and falling back to `OpeningLimitGtdTtlSeconds`.
- `ClobGtdExpirationSecurityBufferSeconds`: extra seconds added to the CLOB wire `expiration` for GTD orders so the local effective deadline honors Polymarket's GTD security threshold; default `60`, valid range `60..300`.
- `PreviousScoreCounterTrendEpsilonScore`: minimum absolute previous-market time-weighted score required before `Prev Score Countertrend` fixed-price, immediate ask-depth, or Premarket variants enter; default `0.0001`.
- `PreviousScoreCounterTrendMinSamples`: minimum archived BTC/ETH/SOL reference samples required from the immediately previous 5-minute market, or from the synthetic 5.5-minute Premarket score window; default `10`.
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

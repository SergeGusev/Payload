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
- `RequireConfiguredDatabase`: set true on VPS/production runs.

## IPC

Keep IPC loopback-only.

- `ListenUrl`: service listener URL.
- `DashboardBaseUrl`: dashboard control URL.

## Execution And Risk

Initial live trading requires:

- `Execution:MakerOnly=true`
- `Execution:AllowTaker=false`

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
- `LeaderboardPages`: number of 50-row pages to fetch, max `21`.
- `CandidatesPerSide`: best-PnL and worst-PnL candidates to enrich.
- `TradesPerCandidate`: recent trades to fetch for each candidate.
- `PositionsPerCandidate`: current positions to fetch for each candidate.
- `RequestDelayMilliseconds`: small delay between Data API requests.

## Analytics

Controls daily report generation, dashboard report limits, and CSV export directory.

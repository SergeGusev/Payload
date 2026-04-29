# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 03 — Configuration, SQLite storage, and logging

## Goal

Add durable configuration, SQLite persistence, and structured logging.

## Scope

This task creates storage and settings infrastructure. No Polymarket API implementation yet except configuration URLs.

## Configuration requirements

Create strongly typed config classes:

```text
BotOptions
RiskOptions
ExecutionOptions
PolymarketOptions
WatchlistOptions
TraderRuleOptions
PaperTradingOptions
DashboardOptions
```

Example configuration:

```json
{
  "Bot": {
    "Mode": "Paper",
    "PollIntervalSeconds": 10,
    "UseWebSockets": false,
    "EnableLiveTrading": false
  },
  "Polymarket": {
    "DataApiBaseUrl": "https://data-api.polymarket.com",
    "ClobBaseUrl": "https://clob.polymarket.com",
    "GammaBaseUrl": "https://gamma-api.polymarket.com",
    "GeoblockUrl": "https://polymarket.com/api/geoblock"
  },
  "PaperTrading": {
    "InitialBankrollUsd": 10000,
    "DefaultOrderTtlSeconds": 300
  },
  "Execution": {
    "MakerOnly": true,
    "AllowTaker": false,
    "MaxSlippageCents": 1,
    "MaxSpreadCents": 2,
    "MaxSpreadPct": 3.0,
    "MinLeaderTradeUsd": 500
  },
  "Risk": {
    "MaxTradeBankrollPct": 0.25,
    "MaxMarketBankrollPct": 1.0,
    "MaxTraderBankrollPct": 3.0,
    "MaxCategoryBankrollPct": 7.5,
    "MaxTotalDeployedPct": 25.0,
    "MaxDailyLossPct": 1.0
  },
  "Watchlist": [
    {
      "Name": "Gopfan",
      "Wallet": "0xPLACEHOLDER",
      "AllowedCategories": ["POLITICS", "WEATHER"],
      "Enabled": true,
      "MaxLagSeconds": 300,
      "MaxSlippageCents": 1,
      "MaxSpreadCents": 2,
      "MaxSpreadPct": 3.0,
      "MinLeaderTradeUsd": 500
    }
  ]
}
```

## SQLite schema

Create schema initialization code. Use either EF Core migrations or hand-written SQL. For MVP, hand-written schema is acceptable.

Required tables:

```text
Traders
TraderRules
LeaderTrades
LeaderPositions
Markets
OrderBookSnapshots
Signals
SignalRejections
PaperOrders
PaperFills
PaperPositions
RiskEvents
BotSettings
ApiErrors
ServiceHeartbeats
```

## Important table design

### `LeaderTrades`

Store enough to deduplicate and audit:

```text
Id
TraderWallet
TraderName
ConditionId
AssetId
MarketSlug
MarketTitle
Outcome
Side
Price
Size
CashValueUsd
TimestampUtc
TransactionHash
RawJson
CreatedAtUtc
```

### `Signals`

```text
Id
LeaderTradeId
TraderWallet
ConditionId
AssetId
Outcome
LeaderPrice
BestBid
BestAsk
SpreadAbs
SpreadPct
LagSeconds
Score
Decision
ProposedPaperPrice
CreatedAtUtc
RawContextJson
```

### `SignalRejections`

```text
Id
SignalId
ReasonCode
ReasonDetails
CreatedAtUtc
```

### `PaperOrders`

```text
Id
SignalId
Status
Side
AssetId
ConditionId
Price
SizeShares
NotionalUsd
CreatedAtUtc
ExpiresAtUtc
FilledAtUtc
CancelledAtUtc
RawDecisionJson
```

## Logging

Use Serilog with:

- rolling file logs
- structured properties
- separate log levels for scanner, API, strategy, risk, paper trading

Do not log secrets. Add a `SecretRedactor` helper even before auth exists.

## Acceptance criteria

1. App loads config into typed options.
2. Invalid config fails fast with clear error messages.
3. SQLite database initializes on startup.
4. Heartbeats are written by the service.
5. Dashboard or CLI can display current config summary without secrets.
6. Logs are written to local files.
7. Unit tests cover config validation and database initialization.

## What to avoid

- Do not store private keys.
- Do not create live order tables yet beyond placeholders if needed.
- Do not ignore config validation failures.

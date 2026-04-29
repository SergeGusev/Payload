# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 04 — Polymarket public API clients

## Goal

Implement typed, read-only C# clients for public Polymarket APIs needed for monitoring and paper trading.

## Scope

Read-only APIs only. No authentication. No order placement. No private keys.

## Official docs

- Trades endpoint: https://docs.polymarket.com/api-reference/core/get-trades-for-a-user-or-markets
- Positions endpoint: https://docs.polymarket.com/api-reference/core/get-current-positions-for-a-user
- Leaderboard endpoint: https://docs.polymarket.com/api-reference/core/get-trader-leaderboard-rankings
- Order book endpoint: https://docs.polymarket.com/api-reference/market-data/get-order-book
- Geoblock endpoint: https://docs.polymarket.com/api-reference/geoblock

## Clients to implement

```text
IPolymarketDataApiClient
  GetTraderLeaderboardAsync(...)
  GetUserTradesAsync(...)
  GetUserPositionsAsync(...)

IPolymarketClobPublicClient
  GetOrderBookAsync(tokenId)
  GetServerTimeAsync()
  GetMarketInfoAsync(conditionId) if supported/needed
  GetMidpointAsync(tokenId) if useful
  GetSpreadAsync(tokenId) if useful

IPolymarketGeoClient
  GetGeoblockStatusAsync()
```

## Trades endpoint requirements

When fetching user trades, explicitly support `takerOnly=false`. This is important because the default may only return taker-side activity, while a strong trader can receive maker fills.

The client should capture fields such as:

```text
proxyWallet
side
asset
conditionId
size
price
timestamp
title
slug
outcome
transactionHash
```

Also store raw JSON for diagnostics.

## Positions endpoint requirements

Capture fields such as:

```text
proxyWallet
asset
conditionId
size
avgPrice
initialValue
currentValue
cashPnl
percentPnl
totalBought
realizedPnl
curPrice
title
slug
outcome
oppositeAsset
endDate
negativeRisk
```

## Order book requirements

Capture:

```text
bids
asks
min_order_size
tick_size
neg_risk
last_trade_price
```

Normalize bids and asks into typed price levels:

```csharp
public sealed record OrderBookLevel(decimal Price, decimal Size);
```

Add computed properties:

```text
BestBid
BestAsk
SpreadAbs
SpreadPct
IsCrossed
HasEnoughDepth
```

## HTTP resilience

Implement:

- configurable timeout
- retry for transient 5xx/429 with backoff
- clear error logging
- no infinite retry loops
- raw response logging only in debug mode or in sanitized form

## Acceptance criteria

1. Public API clients compile and are injected through DI.
2. Methods return typed models.
3. Clients handle empty/invalid responses gracefully.
4. Unit tests cover JSON parsing using saved sample payloads.
5. Scanner can call clients in read-only mode without authentication.
6. API errors are persisted into `ApiErrors`.

## What to avoid

- Do not implement authenticated endpoints.
- Do not place orders.
- Do not assume a field is always present unless documentation guarantees it.
- Do not hide API failures from the strategy engine.

# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 06 — SignalEngine and RiskEngine

## Goal

Implement the strategy layer that turns leader trades into accepted or rejected copy-signal decisions.

## Scope

No real orders. This task produces decisions and proposed paper orders only.

## SignalEngine inputs

```text
LeaderTrade
TraderRule
MarketInfo if available
OrderBookSnapshot
Current paper portfolio/exposure
BotOptions
ExecutionOptions
RiskOptions
```

## SignalEngine output

```csharp
public sealed record SignalDecision(
    bool Accepted,
    int Score,
    string DecisionCode,
    IReadOnlyList<string> Reasons,
    decimal? ProposedPrice,
    decimal? ProposedSizeShares,
    decimal? ProposedNotionalUsd,
    DateTimeOffset CreatedAtUtc);
```

## Default rejection rules

Reject if:

- trader is disabled
- market category is not allowed for this trader
- leader trade side is not supported yet
- signal is too old
- leader trade notional is below threshold
- order book has no bid or ask
- spread exceeds absolute limit
- spread exceeds percentage limit
- current entry would be too far from leader price
- no safe maker price exists
- risk limits would be exceeded
- market is too close to known resolution/event if known

## Initial side support

Only copy `BUY` signals in MVP.

Do not copy `SELL` as new short/opposite positions. For `SELL`, generate an informational event that may later feed exit logic.

## Maker-style proposed price

Given:

```text
leaderPrice = 0.74
bestBid = 0.73
bestAsk = 0.76
tick = 0.01
maxEntry = leaderPrice + 0.01 = 0.75
```

Proposed maker price:

```text
min(bestBid + tick, maxEntry, bestAsk - tick)
```

Result:

```text
0.74
```

If proposed price is not strictly below best ask, reject as `no_safe_maker_price`.

If proposed price is above max entry, reject as `price_moved_too_far`.

## Scoring model

Implement configurable scoring, with these defaults:

```text
+30 category allowed
+20 signal age < 10 seconds
+12 signal age < 60 seconds
+5  signal age < 5 minutes
+20 current entry <= leader price + 0.005
+15 current entry <= leader price + 0.01
+5  current entry <= leader price + 0.02
+15 leader trade is large
+10 order book depth acceptable
+5  slow/non-live market
-20 borderline spread
```

Acceptance threshold defaults:

```text
score < 60: ignore/log only
60-74: observe only
75-89: create small paper order
90+: create normal paper order
```

## RiskEngine rules

Check:

```text
MaxTradeBankrollPct
MaxMarketBankrollPct
MaxTraderBankrollPct
MaxCategoryBankrollPct
MaxTotalDeployedPct
MaxDailyLossPct
MaxOpenOrders
MaxOrderAge
```

RiskEngine should return:

```text
Allowed/Rejected
Reason codes
Allowed size
Exposure after proposed order
```

## Reason codes

Use stable reason codes, not just free text:

```text
trader_disabled
category_not_allowed
unsupported_side
trade_too_old
leader_trade_too_small
missing_orderbook
spread_too_wide_abs
spread_too_wide_pct
price_moved_too_far
no_safe_maker_price
risk_trade_limit
risk_market_limit
risk_trader_limit
risk_category_limit
risk_total_deployed_limit
market_too_close_to_event
```

## Acceptance criteria

1. `SignalEngine` creates accepted/rejected decisions.
2. All rejections are persisted with reason codes.
3. RiskEngine limits are enforced.
4. Unit tests cover the maker price formula.
5. Unit tests cover spread and slippage rejection.
6. Unit tests cover risk limit rejection.
7. No live order placement exists.

## What to avoid

- Do not silently accept signals without an order book.
- Do not chase price.
- Do not let UI code make strategy decisions.
- Do not hardcode one trader’s rules in strategy code.

# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 11 — Analytics and reporting

## Goal

Add reporting that helps decide whether the copy-signal strategy is actually working before any live trading.

## Scope

Analytics over stored leader trades, signals, rejections, paper orders, and paper positions.

## Required reports

### 1. Daily summary

```text
Signals observed
Signals accepted
Signals rejected
Paper orders created
Paper fills
Paper expired orders
Paper PnL
Open paper exposure
Top rejection reasons
API errors
```

### 2. Trader performance

By tracked trader:

```text
Signals
Acceptance rate
Fill rate
Average lag
Average leader price vs proposed price
Paper PnL
Paper PnL by category
Rejection reasons
```

### 3. Category performance

```text
Category
Signals
Accepted
Filled
Paper PnL
Average spread
Average lag
```

### 4. Execution quality

```text
Leader price
Proposed price
Paper fill price
Price difference
Lag seconds
Spread at signal
Mid/bid/ask after 1m, 5m, 30m if available
```

### 5. Rejection analysis

Show whether the bot is missing too many trades because of:

```text
price_moved_too_far
spread_too_wide_abs
spread_too_wide_pct
category_not_allowed
risk limit
trade_too_old
```

## Export

Implement CSV export from dashboard or CLI for:

```text
LeaderTrades
Signals
SignalRejections
PaperOrders
PaperPositions
DailyReports
```

## Acceptance criteria

1. Dashboard has analytics screen or exportable reports.
2. Daily report is generated automatically.
3. Reports can be exported to CSV.
4. Reports use stored data, not live API calls only.
5. README explains how to interpret paper results.

## What to avoid

- Do not overstate paper trading accuracy.
- Do not hide rejected signals from analysis.
- Do not calculate PnL using optimistic ask prices for long exits.

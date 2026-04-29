# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 08 — WPF dashboard

## Goal

Build a WPF dashboard for real-time monitoring of the bot, paper strategy, and risk state.

## Scope

UI only. The dashboard should not contain core strategy logic. The service must keep running if the UI is closed.

## UI architecture

Use MVVM:

```text
Views
ViewModels
Services
Models/DTOs
```

Use `CommunityToolkit.Mvvm` for commands and observable properties.

## Main screens

### 1. Overview

Show:

```text
Mode: ReadOnly / Paper / LiveDisabled / Live
Service status
Last heartbeat
API status
Geoblock status
Paper bankroll
Open paper exposure
Daily paper PnL
Open paper orders
Open paper positions
Kill switch / Pause / Resume buttons as placeholders
```

### 2. Watchlist

Show:

```text
Trader name
Wallet
Enabled
Allowed categories
Last successful scan
Last seen trade
Trades fetched
New trades stored
Error status
```

### 3. Leader Trades

Show table:

```text
Timestamp
Trader
Market
Outcome
Side
Leader price
Size
Cash value
Category
Transaction hash
```

### 4. Signals

Show:

```text
Timestamp
Trader
Market
Outcome
Score
Accepted/rejected
Decision code
Reason codes
Leader price
Best bid
Best ask
Spread abs
Spread pct
Lag seconds
Proposed paper price
```

### 5. Paper Orders

Show:

```text
Status
Side
Market
Outcome
Price
Size shares
Notional
Created
Expires
Filled
TTL remaining
Signal id
```

### 6. Paper Positions

Show:

```text
Market
Outcome
Size
Average price
Current bid/ask
Estimated value
Unrealized PnL
Realized PnL
Source trader
```

### 7. Risk

Show:

```text
Risk limits
Current usage by market/trader/category
Daily loss
Total deployed
Risk events
```

### 8. Logs

Show latest logs and API errors:

```text
Time
Severity
Component
Message
Details
```

## Live updates

For MVP, polling the SQLite database every 1-3 seconds is acceptable. Later, replace or augment with IPC/WebSocket from the service.

## Commands

Implement UI commands as placeholders if service IPC is not ready:

```text
Pause scanning
Resume scanning
Disable trader
Enable trader
Cancel paper order
Clear logs view
Export CSV
```

Do not implement live order commands yet.

## Acceptance criteria

1. Dashboard opens and shows current data from SQLite.
2. Dashboard refreshes without blocking UI.
3. User can inspect signals and rejection reasons.
4. User can inspect paper orders and positions.
5. No strategy decisions are made inside WPF view models.
6. UI handles empty database gracefully.
7. README includes screenshots or a description of screens.

## What to avoid

- Do not make service depend on dashboard.
- Do not store secrets in dashboard.
- Do not put API polling in UI if it belongs in service.
- Do not block UI thread with database/API work.

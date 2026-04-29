# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


## Codex task

Read this project brief and create or update the repository plan. Do not implement live trading in this task unless another specific task asks for it.

## Product name

`PolyCopyTrader`

## Goal

Build a Windows/.NET C# application that:

1. Monitors selected profitable Polymarket traders.
2. Detects their recent trades and current open positions.
3. Evaluates whether a leader trade is worth copying.
4. Initially runs in read-only and paper-trading mode.
5. Later can place tiny maker-only live orders after explicit enablement.
6. Runs 24/7 on a Windows VPS.
7. Provides a WPF dashboard for real-time monitoring.

## Non-goals for MVP

- No real order placement.
- No private key handling.
- No deposits/withdrawals.
- No automatic wallet management.
- No blind copy of every trade.
- No crypto/live-sports high-frequency copy mode.

## Product philosophy

This is a **copy-signal bot**. The bot should never treat a leader trade as an automatic order. It must answer:

- Is this trader strong in this category?
- Is the signal fresh?
- Is the current price still near the leader price?
- Is the spread acceptable?
- Is the order book deep enough?
- Is the market too close to a known event/resolution?
- Does this violate any risk limit?

Only if checks pass should the bot create a paper order or, in future phases, a live maker-only order.

## Preferred stack

- C# / latest stable .NET LTS.
- WPF for dashboard.
- .NET Worker Service for background engine.
- SQLite for MVP storage.
- Serilog for logging.
- CommunityToolkit.Mvvm for MVVM.
- System.Net.Http for REST.
- System.Net.WebSockets for WebSocket.
- Nethereum or equivalent C# crypto library only when authenticated signing tasks begin.

## High-level architecture

```text
PolyCopyTrader.Service
  Background worker, scanner, strategy, risk, paper trading, later execution.

PolyCopyTrader.Dashboard
  WPF UI showing status, watchlist, trades, signals, orders, positions, logs.

PolyCopyTrader.Polymarket
  Public API clients, WebSocket clients, later authenticated clients.

PolyCopyTrader.Strategy
  SignalEngine, RiskEngine, PaperTradingEngine, ExitEngine.

PolyCopyTrader.Storage
  SQLite schema, repositories, migrations/schema init.

PolyCopyTrader.Domain
  Domain models, enums, value objects.
```

## Strategy default configuration

```text
Mode: Paper
Live trading: disabled
Taker orders: disabled
Maker-style post-only GTD simulation: enabled
Max slippage from leader price: 1 cent
Max absolute spread: 2 cents
Max spread percentage: 3%
Max paper trade size: 0.25% of paper bankroll
Max exposure per market: 1% of paper bankroll
Max exposure per copied trader: 3%
Max exposure per category: 7.5%
Max total deployed capital: 25%
```

## Watchlist idea

Each tracked trader has its own category allowlist and thresholds.

Example:

```json
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
```

## Core entities

- `TraderProfile`
- `TraderRule`
- `LeaderTrade`
- `LeaderPosition`
- `MarketInfo`
- `OrderBookSnapshot`
- `Signal`
- `SignalRejection`
- `RiskDecision`
- `PaperOrder`
- `PaperFill`
- `PaperPosition`
- `BotState`
- `ApiError`

## Acceptance criteria for the whole project

The full project is successful when:

1. The app can run read-only + paper trading for weeks without manual intervention.
2. Dashboard shows real-time status and decisions.
3. All leader trades and bot decisions are persisted.
4. Rejected signals have explicit reasons.
5. Paper trading produces realistic enough metrics to evaluate the strategy.
6. Live trading remains impossible unless explicitly enabled.
7. Auth/signing code has deterministic tests before any live order support.
8. Live trading, when later enabled, is maker-only, tiny-size, kill-switch protected, and disabled by default.

## Important documentation links

- Codex prompting: https://developers.openai.com/codex/prompting
- AGENTS.md guide: https://developers.openai.com/codex/guides/agents-md
- Polymarket CLOB V2 migration: https://docs.polymarket.com/v2-migration
- Polymarket authentication: https://docs.polymarket.com/api-reference/authentication
- Polymarket trades: https://docs.polymarket.com/api-reference/core/get-trades-for-a-user-or-markets
- Polymarket positions: https://docs.polymarket.com/api-reference/core/get-current-positions-for-a-user
- Polymarket order book: https://docs.polymarket.com/api-reference/market-data/get-order-book

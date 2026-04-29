# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 10 — WebSocket market data

## Goal

Add WebSocket-based market data monitoring for markets that are relevant to current signals, paper orders, or paper positions.

## Scope

Public market WebSocket first. User WebSocket can be prepared but not authenticated until later tasks. No live trading.

## Official docs

- WebSocket overview: https://docs.polymarket.com/market-data/websocket/overview
- Market channel: https://docs.polymarket.com/api-reference/wss/market
- User channel: https://docs.polymarket.com/api-reference/wss/user

## Subscription policy

Do not subscribe to everything. Subscribe only to assets that matter:

```text
- assets from currently pending paper orders
- assets from open paper positions
- assets from newly detected strong signals
- assets manually pinned in dashboard
```

## Market events to support

Support and persist/dispatch:

```text
book snapshots
price changes
trade executions if provided
market resolved events if provided
connection status
reconnect events
```

## WebSocket service requirements

```text
- automatic reconnect with backoff
- resubscribe after reconnect
- heartbeat/ping handling if required
- stale data detection
- graceful cancellation
- sanitized logs
```

## Updating paper trading

Use WebSocket updates to improve paper fill simulation:

- update best bid/ask in near real time
- evaluate pending paper orders on book/trade updates
- update paper position estimated value

## Dashboard updates

Dashboard should show:

```text
WebSocket connected/reconnecting/disconnected
Subscribed assets count
Last message time
Last book update per market
```

## Acceptance criteria

1. Public market WebSocket connects.
2. Bot subscribes only to relevant asset IDs.
3. Reconnect works.
4. Order book updates feed paper trading.
5. Dashboard shows WebSocket status.
6. Unit tests cover event parsing with sample messages.
7. No authenticated user channel is required yet.
8. No live orders are placed.

## What to avoid

- Do not subscribe to all Polymarket markets.
- Do not block scanner loop on WebSocket failures.
- Do not let stale WebSocket data be used without detection.

# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 12 — Testing, QA, and hardening before any auth/live work

## Goal

Make the read-only + paper-trading system reliable enough to run unattended before adding authenticated trading.

## Scope

Tests, diagnostics, resilience, and bug fixing. No auth. No live trading.

## Unit tests required

### SignalEngine

Test:

- category allowed/rejected
- stale signal rejected
- unsupported side rejected
- spread abs rejected
- spread pct rejected
- price moved too far rejected
- maker price formula
- score thresholds

### RiskEngine

Test:

- trade size limit
- market exposure limit
- trader exposure limit
- category exposure limit
- total deployed exposure limit
- daily loss limit

### PaperTradingEngine

Test:

- order creation
- expiration
- approximate fill
- partial fill model if implemented
- average price calculation
- conservative valuation using bid

### API clients

Test parsing for saved sample JSON for:

- trades
- positions
- order book
- leaderboard
- geoblock

## Integration tests

Add tests with fake API clients:

```text
watchlist scan -> leader trade stored -> signal generated -> paper order created -> paper fill -> paper position
```

## Resilience checks

Simulate:

- API 429
- API 500
- malformed response
- missing order book
- database temporarily locked
- invalid watchlist wallet
- WebSocket disconnect if task 10 is complete

## Runtime diagnostics

Add a diagnostics screen or CLI command showing:

```text
config summary
service status
scanner status
latest API errors
database path
watchlist summary
current mode
risk usage
```

## Acceptance criteria

1. Test suite covers strategy/risk/paper logic.
2. App survives fake API failures.
3. App does not crash on invalid trader config.
4. Dashboard shows diagnostics.
5. README has troubleshooting section.
6. Codex should not proceed to auth/live tasks until this passes.

## What to avoid

- Do not start live/auth work while core paper mode is unstable.
- Do not rely only on manual testing.
- Do not swallow errors without logs.

# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 07 — PaperTradingEngine

## Goal

Implement realistic-enough paper trading for maker-style copy-signal orders.

## Scope

Paper orders and paper positions only. No live orders. No private keys.

## Paper order lifecycle

Statuses:

```text
Pending
PartiallyFilled
Filled
Expired
Cancelled
Rejected
```

For MVP, partial fills can be simplified but should be represented in the model.

## Creating paper orders

When SignalEngine accepts a signal:

1. Create a `PaperOrder`.
2. Use proposed maker price.
3. Use proposed size from RiskEngine.
4. Set TTL from config, default 300 seconds.
5. Persist order.
6. Display in dashboard.

## Approximate fill simulation

MVP fill simulation may be approximate. The goal is to avoid over-claiming precision.

Acceptable initial rules:

- If a future observed trade price crosses the paper order price, mark fill as possible.
- If future best ask/bid indicates the order would likely have been marketable, mark fill as possible.
- Mark fills as `SimulatedApproximate`.
- Store the evidence used for fill.

For a paper BUY maker order:

```text
If observed bestAsk <= paperBuyPrice, or observed trade price <= paperBuyPrice,
then simulated fill may occur.
```

For a paper SELL maker order, when added later:

```text
If observed bestBid >= paperSellPrice, or observed trade price >= paperSellPrice,
then simulated fill may occur.
```

## Conservative fill policy

Prefer under-filling to over-filling. If uncertain, keep order pending or mark `UncertainNotFilled`.

Rationale: in real markets, other orders may be ahead in queue.

## Position accounting

For filled paper BUY:

```text
Position size += filled shares
Average price updates by weighted average
Cash deployed += price * shares
```

For paper PnL:

```text
Unrealized value = size * current bid, not midpoint
```

Use bid for valuing long outcome shares because bid is the immediate exit price.

## Expiration

When `ExpiresAtUtc < now` and not fully filled:

```text
Pending -> Expired
PartiallyFilled -> keep fill and expire remaining quantity
```

## Paper exit placeholders

For MVP, implement placeholders for exit logic but do not overcomplicate.

Possible future exit reasons:

```text
leader_reduced_position
leader_exited
take_profit
risk_reduction
market_near_resolution
manual_dashboard_action
```

## Acceptance criteria

1. Accepted signals create paper orders.
2. Paper orders expire after TTL.
3. Approximate fills can be simulated from observed market data.
4. Filled paper orders create/update paper positions.
5. Paper PnL is calculated conservatively using bid for long positions.
6. Dashboard can display paper orders and positions.
7. Unit tests cover paper order lifecycle and average price calculation.
8. No live orders are placed.

## What to avoid

- Do not assume touching a price guarantees fill without marking approximation.
- Do not value long positions at ask.
- Do not claim paper PnL is exact.
- Do not create hidden live-trading paths.

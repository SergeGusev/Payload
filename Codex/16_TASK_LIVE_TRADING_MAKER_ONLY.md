# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 16 — Live trading, maker-only, tiny-size, manually enabled

## Goal

Add live order placement in the safest possible initial form: maker-only, GTD, tiny size, manual enablement, kill-switch protected.

## Scope

This task may place real orders only after explicit manual enablement and only with tiny configured size. This is the first task where live trading is allowed.

## Prerequisites

- Tasks 1–12 complete and stable.
- Paper trading has run long enough to produce useful statistics.
- Tasks 13–15 complete.
- Auth/signing tests pass.
- Separate trading wallet configured.
- Live trading explicitly enabled in config and UI.

## Live trading defaults

```text
EnableLiveTrading: false
MakerOnly: true
AllowTaker: false
DefaultOrderType: GTD
DefaultTTL: 300 seconds or less
MaxTradeBankrollPct: 0.10 initially
MaxMarketBankrollPct: 0.50 initially
MaxDailyLossPct: 0.50 initially
MaxTotalDeployedPct: 5.0 initially
```

## Required controls

- global kill switch
- cancel all orders
- pause new live orders
- close live mode back to paper mode
- max daily loss lockout
- max order count
- max stale order age
- geoblock check before any order
- server time / clock drift check
- API error lockout

## Order placement rules

Allowed initially:

```text
BUY only
GTD only
post-only/maker-only only
no taker
no FOK/FAK
no crypto
no live sports
```

Before placing order:

1. Re-fetch order book.
2. Re-run SignalEngine.
3. Re-run RiskEngine.
4. Verify proposed price is still safe.
5. Verify order will not cross spread.
6. Verify live trading still enabled.
7. Verify geoblock status.
8. Verify not in daily lockout.

## Post-order tracking

Persist:

```text
LiveOrder
LiveOrderStatus
OrderId
SubmittedAtUtc
ResponseStatus
FilledSize
RemainingSize
CancelStatus
RawResponse redacted
```

Use user channel or polling to track status.

## Cancel behavior

Cancel when:

- TTL expires if not automatically expired
- strategy pauses
- kill switch triggered
- stale market data
- WebSocket disconnected too long
- price/fairness context changes materially
- dashboard manual cancel

## Acceptance criteria

1. Live trading cannot happen unless explicitly enabled.
2. Live order size is tiny and configurable.
3. Orders are maker-only GTD.
4. Taker mode is unavailable/disabled.
5. Kill switch cancels open orders and pauses new ones.
6. Geoblock check prevents order placement when blocked.
7. Every live action is logged and persisted.
8. Tests cover live order gating logic.

## What to avoid

- Do not enable live trading by default.
- Do not implement taker orders in this task.
- Do not increase sizes automatically.
- Do not allow live trading from blocked VPS locations.
- Do not place orders if order book is stale.

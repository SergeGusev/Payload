# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 18 — Operations runbook

## Goal

Create an operational playbook for running, monitoring, and safely maintaining PolyCopyTrader.

## Scope

Documentation and dashboard/runbook improvements. No new trading features required.

## Create docs

```text
docs/runbook.md
docs/incident_response.md
docs/live_trading_checklist.md
docs/paper_trading_evaluation.md
docs/configuration_reference.md
```

## Runbook sections

### Daily checklist

```text
- service running
- dashboard connects
- last heartbeat recent
- API errors normal
- geoblock OK
- paper/live mode as expected
- open exposure within limits
- no stale orders
- DB backup recent
```

### Weekly review

```text
- trader performance
- category performance
- signal acceptance rate
- fill rate
- rejected signal reasons
- paper/live PnL
- API reliability
- strategy parameter changes
```

### Before enabling live trading

Checklist:

```text
- paper trading results reviewed
- max sizes tiny
- live trading disabled by default verified
- separate wallet funded with small amount
- geoblock OK from VPS
- kill switch tested
- cancel-all tested in dry-run/test mode
- signing tests pass
- no stale open orders
```

### Incident response

For each incident, define actions:

```text
API 429 storm
API 5xx storm
WebSocket stale/disconnected
Database locked/corrupt
Dashboard disconnected
Unexpected live order
Daily loss limit triggered
Geoblock blocked
Clock drift
Signing error
```

### Parameter change policy

Document that strategy thresholds should not be changed impulsively.

Any change should record:

```text
old value
new value
reason
expected effect
review date
```

## Dashboard improvements

Add a simple `Runbook` or `Checklist` screen if practical, or link docs from dashboard.

## Acceptance criteria

1. Runbook docs exist.
2. Live trading checklist exists.
3. Incident response doc exists.
4. Configuration reference exists.
5. Paper trading evaluation doc explains how to decide whether strategy is viable.
6. Dashboard links or references these docs.

## What to avoid

- Do not treat paper PnL as exact live PnL.
- Do not increase size without written review.
- Do not leave live trading enabled after maintenance unless intentionally desired.

# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 09 — 24/7 Worker Service and dashboard communication

## Goal

Make the bot engine run reliably as a background service suitable for a Windows VPS, while the WPF dashboard can connect/disconnect independently.

## Scope

Background hosting, health state, pause/resume controls, and optional local IPC. No live trading.

## Service responsibilities

```text
- Load config
- Validate config
- Initialize database
- Start scanner loop
- Evaluate signals
- Create paper orders
- Update paper orders/positions
- Persist heartbeats
- Persist API errors
- Persist risk events
- Expose health/status to dashboard
```

## Windows Service support

Add support for running as:

```text
- normal console app for development
- Windows Service for VPS deployment
```

Use `UseWindowsService()` if appropriate.

## Service states

```text
Starting
Running
Paused
Stopping
Stopped
Error
```

## Pause/resume

Implement safe pause:

```text
PauseScanning
ResumeScanning
PausePaperTrading
ResumePaperTrading
```

For future live trading, pause should also stop creating new orders, but that is only a placeholder in this task.

## Dashboard communication

Choose one MVP approach:

### Option A — database polling

Dashboard reads service status from SQLite every 1-3 seconds.

### Option B — local HTTP endpoint

Service exposes localhost-only endpoints:

```text
GET /health
GET /status
POST /pause
POST /resume
```

If adding local HTTP, bind only to localhost and do not expose externally.

## Heartbeat

Service writes heartbeat every N seconds:

```text
ServiceName
Status
StartedAtUtc
LastHeartbeatUtc
Version
Mode
CurrentLoop
LastError
```

## Error handling

The service should not crash on:

- one API failure
- one malformed market/trade response
- one disabled/invalid trader
- one database insert failure if recoverable

But it should enter safe pause on:

- database unavailable
- repeated API failures
- config invalid
- risk engine fatal error

## Acceptance criteria

1. Service can run as console app.
2. Service can be installed/configured as Windows Service, or README explains how.
3. Dashboard can show service status.
4. Pause/resume works for scanner/paper mode.
5. Heartbeats are persisted.
6. Service recovers from transient API failures.
7. No live trading exists.

## What to avoid

- Do not require RDP session to stay open for service to run.
- Do not let dashboard closure stop the service.
- Do not expose local control endpoints publicly.

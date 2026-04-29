# PolyCopyTrader — Codex task pack

Last verified context date: 2026-04-28. Before implementing authenticated/live trading, re-check the official Polymarket documentation because CLOB/API details can change.

Core idea: this is a cautious **copy-signal** system, not blind copy-trading. Leader trades are signals that must pass filters for category, freshness, price, spread, liquidity, and risk.


# Task 17 — Windows VPS deployment and security

## Goal

Prepare PolyCopyTrader for secure 24/7 operation on a Windows VPS.

## Scope

Deployment scripts, service installation, logging paths, secret handling, monitoring, backup. No strategy changes unless required for safety.

## VPS location warning

Before live trading, the bot must check Polymarket geoblock status from the actual VPS. The user's own country is not enough; the physical VPS IP/location matters.

## Deployment artifacts

Create:

```text
deploy/install-service.ps1
deploy/uninstall-service.ps1
deploy/start-service.ps1
deploy/stop-service.ps1
deploy/backup-db.ps1
deploy/README.md
```

## Service installation

Support installing `PolyCopyTrader.Service` as Windows Service.

Document:

```text
publish command
service creation command
config location
logs location
database location
how to start/stop/restart
how to view logs
```

## Security requirements

- separate trading wallet
- small bankroll
- no secrets in repository
- no secrets in appsettings
- secrets via environment variables or DPAPI/Credential Manager
- RDP locked down
- Windows firewall configured
- logs rotated
- database backed up
- kill switch accessible

## Backup

Implement database backup script:

```text
- use pg_dump or provider-managed PostgreSQL backups
- copy DB to timestamped backup
- keep retention limit
```

## Monitoring

At minimum:

```text
service heartbeat
log file rotation
Telegram/email alert placeholder
local dashboard status
Windows Event Log optional
```

Alerts for:

```text
service stopped
repeated API failures
geoblock blocked
daily loss limit hit
kill switch triggered
database error
websocket disconnect too long
```

## Acceptance criteria

1. App can be published for Windows VPS.
2. Service can be installed/uninstalled.
3. Logs and DB paths are documented.
4. Secret handling is documented.
5. Backup script exists.
6. Geoblock check is part of startup status.
7. README contains VPS deployment instructions.

## What to avoid

- Do not hardcode secrets or paths.
- Do not expose dashboard/control ports publicly.
- Do not rely on interactive desktop session for service operation.

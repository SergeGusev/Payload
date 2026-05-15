# PolyCopyTrader Windows VPS Deployment

These scripts prepare the service for a Windows VPS. They do not store secrets and do
not open public dashboard/control ports.

## Publish And Install

Run from an elevated PowerShell session:

```powershell
.\deploy\install-service.ps1
```

Optional parameters:

```powershell
.\deploy\install-service.ps1 -Configuration Release -PublishDirectory ..\publish\service -Start
```

The script runs `dotnet publish`, installs `PolyCopyTrader.Service` as a Windows
Service, and leaves configuration files in the publish directory. When the repo
is a Git checkout, the script embeds the current short commit in the service
informational version. The running service writes that marker to
`service_heartbeats.version`, for example `info=1.0.0+9785ba3`.

Verify the deployed artifact from PostgreSQL after restart:

```sql
SELECT service_name, version, started_at_utc, last_heartbeat_utc
FROM service_heartbeats
WHERE service_name = 'PolyCopyTrader.Service';
```

## Start, Stop, Uninstall

```powershell
.\deploy\start-service.ps1
.\deploy\stop-service.ps1
.\deploy\uninstall-service.ps1
```

## Configuration

Do not store secrets in `appsettings.json`.

Use environment variables or Windows Credential Manager lookup names for:

- `POLYCOPYTRADER_POSTGRES_CONNECTION`
- `POLYCOPYTRADER_POLYMARKET_API_KEY`
- `POLYCOPYTRADER_POLYMARKET_API_KEY_OWNER`
- `POLYCOPYTRADER_POLYMARKET_API_SECRET`
- `POLYCOPYTRADER_POLYMARKET_API_PASSPHRASE`
- `POLYCOPYTRADER_POLYMARKET_ORDER_SIGNING_PRIVATE_KEY`

Keep `Ipc:ListenUrl` and `Ipc:DashboardBaseUrl` on loopback, for example
`http://127.0.0.1:5118/`. Do not expose IPC through the VPS firewall.

## Net48 Secret Transfer Without WinRM

If WinRM authorization is blocked but the current machine already has the Net48
secrets, export an encrypted transfer package instead of writing plaintext
secrets into a script:

```powershell
.\scripts\Export-Net48-SecretsPackage.ps1
```

Choose a one-time transfer package password. Copy the generated encrypted
package from `artifacts\net48-secret-transfer` plus
`scripts\Import-Net48-SecretsPackage.ps1` to the target machine. On the target,
run PowerShell as Administrator:

```powershell
.\Import-Net48-SecretsPackage.ps1 -PackagePath .\polycopytrader-net48-secrets.enc.json
```

The importer prompts for the same transfer package password and writes
machine-level environment variables. It does not print secret values. Delete the
encrypted package after the target smoke checks pass.

## Logs

Service logs are written under the service output directory:

```text
publish\service\logs\polycopytrader-service-*.log
```

Logs rotate daily through Serilog.

## PostgreSQL Backup

Install PostgreSQL client tools so `pg_dump` is available, then run:

```powershell
.\deploy\backup-db.ps1
```

The script reads `POLYCOPYTRADER_POSTGRES_CONNECTION`, writes timestamped custom-format
backups under `backups`, and removes backups older than the configured retention.
Provider-managed PostgreSQL backups are also acceptable, but keep at least one
restore-tested backup path.

## Security Checklist

- Use a separate Polymarket trading wallet with a tiny bankroll.
- Keep live trading disabled unless intentionally testing tiny orders.
- Verify geoblock status from the VPS IP, not from your local workstation.
- Restrict RDP by source IP where possible and use strong account credentials.
- Keep Windows Firewall enabled.
- Keep IPC loopback-only.
- Store secrets outside the repository.
- Run `.\scripts\qa-check.ps1` after deployment changes.
- Test the dashboard kill switch and live cancel-all before any live order.

## Monitoring

At minimum, monitor:

- Windows Service state.
- Latest service heartbeat in the dashboard.
- `logs\polycopytrader-service-*.log`.
- Dashboard Diagnostics tab.
- Startup geoblock live event.
- API errors, websocket stale/disconnected state, database errors, kill-switch events,
  and daily-loss lockout events.

Telegram/email alerts are not implemented yet; use the dashboard and Windows Service
monitoring as the baseline.

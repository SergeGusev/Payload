# PolyCopyTrader Runbook

This runbook is the operating checklist for the Windows service, dashboard, database,
and trading controls.

## Daily Checklist

- Confirm the Windows Service is running.
- Open the dashboard and confirm it connects to storage.
- Check the latest service heartbeat is recent.
- Confirm mode is the intended mode: `ReadOnly`, `Paper`, `DryRun`, or intentionally `Live`.
- Review API errors and confirm they are normal for the period.
- Confirm startup geoblock status is OK from the actual host.
- Check WebSocket status and stale/disconnect indicators.
- Review open paper and live exposure against configured limits.
- Confirm no stale paper or live orders remain open.
- Confirm the latest database backup is recent.
- Confirm kill switch is not active unless intentionally engaged.

## Weekly Review

- Review trader performance reports.
- Review category performance reports.
- Review signal acceptance rate.
- Review paper fill rate and expired-order count.
- Review top rejection reasons.
- Review paper PnL and open exposure.
- Review any live order records if live trading was enabled.
- Review API reliability and WebSocket reconnect count.
- Decide whether any strategy parameters deserve a written change proposal.

## Maintenance Window

Before maintenance:

- Pause scanning.
- Pause paper trading if modifying storage or paper engine code.
- Pause live trading.
- Confirm no live orders are open, or run cancel-all live.
- Run a database backup.

After maintenance:

- Run `.\scripts\qa-check.ps1`.
- Start or resume the service.
- Confirm dashboard heartbeat and startup geoblock event.
- Resume subsystems intentionally; do not resume live trading by habit.

## Parameter Change Policy

Do not change thresholds impulsively after a short losing or winning streak. Record:

- old value;
- new value;
- reason;
- expected effect;
- review date;
- whether the change is paper-only, dry-run, or live.

Keep size changes especially conservative. Any live size increase requires a written
review and a new observation period.

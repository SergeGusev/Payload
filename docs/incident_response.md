# Incident Response

The default incident action is: pause live trading first, preserve logs/database state,
then diagnose.

## API 429 Storm

- Pause live trading.
- Leave scanner running only if rate limits are mild.
- Check API error count and affected component.
- Inspect `polymarket_http_logs` for request URLs, UTC timestamps, status codes, and response previews.
- Increase poll intervals only after writing down the change.
- Resume after errors normalize.

## API 5xx Storm

- Pause live trading.
- Keep kill switch available.
- Avoid retries beyond configured limits.
- Inspect `polymarket_http_logs` before changing endpoint, DNS, or TLS settings.
- Resume only after several healthy loops.

## WebSocket Stale Or Disconnected

- Pause live trading if market data is stale.
- Let the service reconnect with backoff.
- Check `market_data_status` and latest log entries.
- If stale persists, restart the service during a maintenance window.

## Database Unavailable Or Corrupt

- Pause all trading modes.
- Stop the service if writes are failing repeatedly.
- Take a filesystem snapshot before repair attempts if possible.
- Restore from the latest tested PostgreSQL backup when needed.
- Do not enable live trading until storage is healthy.

## Dashboard Disconnected

- Check `GET /health` on `http://127.0.0.1:5118/`.
- Confirm dashboard and service use the same IPC URL.
- Confirm IPC remains loopback-only.
- Use service logs and PostgreSQL directly if dashboard is unavailable.

## Unexpected Live Order

- Press kill switch.
- Run cancel-all live.
- Confirm open live orders in dashboard and Polymarket UI.
- Preserve `live_orders`, `live_trading_events`, and service logs.
- Do not resume live trading until the root cause is written down.

## Daily Loss Limit Triggered

- Pause live trading.
- Confirm whether the loss is real, paper-only, or mark-to-market noise.
- Do not raise limits during the same day.
- Review trader/category exposure before resuming another day.

## Geoblock Blocked

- Live trading must remain paused.
- Confirm the VPS IP and country in startup/live events.
- Do not bypass geoblocking.
- Move infrastructure only after legal/regulatory review.

## Clock Drift

- Pause live trading.
- Fix Windows time sync.
- Confirm CLOB server time drift is under `LiveTrading:MaxClockDriftSeconds`.
- Resume only after a successful QA run.

## Signing Error

- Pause live trading.
- Confirm signer address, funder address, signature type, and secret-provider lookup names.
- Verify the configured private key address matches `PolymarketAuth:SigningAddress`.
- Never paste the private key into logs, prompts, screenshots, or repository files.

## Kill Switch Triggered

- Confirm cancel-all live result.
- Confirm live orders are cancelled or otherwise accounted for.
- Clear kill switch only after the incident is understood.
- Resume scanner/paper/live subsystems separately and intentionally.

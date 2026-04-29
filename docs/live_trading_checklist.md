# Live Trading Checklist

Live trading is disabled by default. Use this checklist before any live session.

## Required Preconditions

- Paper trading results have been reviewed over a meaningful sample.
- Dry-run signing produced expected `DryRunSigned` records.
- `dotnet build`, `dotnet test`, `--print-config`, and runtime IPC smoke pass.
- The service runs on the intended VPS.
- Startup geoblock check is OK from the VPS IP.
- PostgreSQL backup was taken and restore path is understood.
- A separate trading wallet is funded with a tiny bankroll only.
- Polymarket UI access is available for manual verification.

## Configuration Gates

- `Bot:Mode` is `Live`.
- `Bot:EnableLiveTrading` is `true`.
- `LiveTrading:ManualEnableCode` is `LIVE_TRADING_ENABLED`.
- `Execution:MakerOnly` is `true`.
- `Execution:AllowTaker` is `false`.
- `LiveTrading:MaxOrderNotionalUsd` is tiny.
- `LiveTrading:MaxOpenLiveOrders` is tiny, initially `1`.
- `PolymarketAuth:SigningAddress` is the signer wallet.
- `PolymarketAuth:FunderAddress` is the funded Polymarket wallet/proxy.
- `PolymarketAuth:SignatureType` is explicitly chosen.
- Secret lookup names point to environment variables or Credential Manager entries.

## Functional Checks

- Dashboard connects.
- Kill switch pauses live trading.
- Cancel-all live command works in a safe test context.
- No stale live orders exist.
- API error lockout is clear.
- WebSocket status is healthy.
- CLOB server time drift is under the configured limit.

## During Live Session

- Watch the dashboard Live Orders and Live Events tabs.
- Keep Polymarket UI open for manual cross-checking.
- Do not change strategy thresholds mid-session.
- Do not increase size after a win or loss.

## After Live Session

- Pause live trading.
- Confirm no open live orders remain.
- Export or snapshot relevant logs.
- Review every live order and event.
- Record whether the session matched expectations.

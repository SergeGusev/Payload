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
- `PaperTrading:RunInLiveMode` is `true` if this session should continue shadow Paper alongside Live.
- `LiveTrading:ManualEnableCode` is `LIVE_TRADING_ENABLED`.
- Follow leader live remains maker-only: `Execution:MakerOnly=true` and `Execution:AllowTaker=false`.
- BTC 5-minute Live-shadow stakes, if enabled per strategy, are intentional BUY-only `GTD` limit orders with `postOnly=false` and `OpeningLimitGtdTtlSeconds` (`120` seconds by default); any immediately marketable portion may fill as taker and the remainder can rest until GTD expiration/cancel/market close.
- `LiveTrading:MaxOrderNotionalUsd` is tiny.
- `LiveTrading:MaxOpenLiveOrders` is tiny, initially `1`.
- `PolymarketAuth:SigningAddress` is the signer wallet.
- `PolymarketAuth:FunderAddress` is the funded Polymarket wallet/proxy.
- `PolymarketAuth:SignatureType` is explicitly chosen.
- Secret lookup names point to environment variables or Credential Manager entries.
- L2 API Credential Manager targets exist. With Live disabled, they can be derived
  or created by running `.\PolyCopyTrader.Service.exe --bootstrap-polymarket-api-credentials`
  from the service output directory.
- `--auth-readiness-smoke` and `--dry-run-signing-smoke` both pass from the same
  output directory and do not print secrets.
- `--clob-authenticated-read-smoke` passes from the same output directory. It
  sends only CLOB `GET /trades`; it does not place or cancel orders.

## Functional Checks

- Dashboard connects.
- Dashboard `Live Readiness` shows no `Blocked` or `Error` rows for the intended live session.
- Kill switch pauses live trading.
- Cancel-all live command works in a safe test context.
- `--clob-cancel-all-smoke` passes only after the operator confirms that all
  open CLOB account orders may be cancelled.
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

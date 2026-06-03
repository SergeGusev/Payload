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
- Paper/Live-shadow stakes are limited to the explicit allow-list: `BTC Up or Down 5m Skip 1`, `BTC Up or Down 5m Middle 1 47 bps Instant`, `BTC Up or Down 5m Binance 10 bps`, `BTC Up or Down 5m Binance 17 bps Instant`, `BTC Up or Down 5m Binance 18 bps`, `BTC Up or Down 5m Binance 19 bps`, `BTC Up or Down 5m Binance 20 bps`, `BTC Up or Down 5m Binance 20 bps Instant`, `BTC Up or Down 5m Binance 21 bps`, `BTC Up or Down 5m Binance 22 bps`, `BTC Up or Down 5m Binance 23 bps`, `ETH Up or Down 5m Skip 7 bps Instant`, `SOL Up or Down 5m Binance 24 bps Instant`, and `SOL Up or Down 5m Skip 42 bps Instant`.
- Paper/Live-shadow stakes, if enabled per strategy, are intentional BUY-only `GTD` limit orders with `postOnly=false`; by default local cancellation is `OpeningLimitExpireBeforeMarketEndSeconds` (`60`) seconds before market close, while the CLOB wire expiration includes `ClobGtdExpirationSecurityBufferSeconds` (`60`). Any immediately marketable portion may fill as taker and the remainder can rest until GTD expiration/cancel/market close.
- Dashboard `Paper Lost` / `Paper Cnt` and `Live Lost` / `Live Cnt` are stored separately per strategy. Both modes apply the same loss-counter stake add-on from their own data: while the matching counter is positive, add `Stake * min(Cnt, 2)`, so the final stake is capped at three original stakes.
- Paper/Live-shadow matching must keep asset, condition, outcome, order type, `postOnly=false`, and limit price within `0.000001`; Paper and Live requested sizes may differ because Paper and Live stake/add-on sizing use separate base stake and counter fields. Shape mismatch disables `LiveStakes` for that strategy and cancels correlated open live orders.
- Plain-text CLOB error bodies are normalized before storage in `live_orders.raw_response_json`; Paper/Live-shadow persistence failures are logged and trigger cancellation of the affected submitted order when possible, but they no longer clear the strategy `Live` flag. Risk failures such as insufficient strategy live balance and critical Paper/Live shape mismatch still disable `LiveStakes`.
- `LiveTrading:MaxOrderNotionalUsd` is a hard emergency ceiling, not the normal
  stake-sizing control. Intended stake sizing is set per strategy through
  Dashboard `Live $`, optional `Live Lost` add-on, and `Live bal`.
- `LiveTrading:MaxOpenLiveOrders` remains conservative, initially `1`.
- `LiveTrading:AutoLivePauseStrategies` may be left empty. Add specific
  strategy codes only when those strategies should auto-pause Live after recent
  Live losses and resume from positive recent Paper evidence.
- On service startup, stored `auto_live_paused=true` rows are cleared for
  strategies outside the current `AutoLivePauseStrategies` list.
- For Paper/Live-shadow, `LiveTrading` market/total exposure caps are
  checked against open Live orders only; Paper backlog must still be monitored
  separately, but it must not consume Live safety ceilings.
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

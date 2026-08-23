# 2026-08-23

## 2026-08-23T06:11:02.191920Z
Request:
Сможешь исправить ситуацию с Maker‑GTD  ?

Result:
Focused read-only production and source diagnosis proved that exact ETH Maker-GTD evidence timeouts are caused by queue waiting: the last 100 timeout expirations had matching event ages of 82,156.7825..261,849.3178 ms and total pending snapshots up to 157,760, while individual in-flight phases generally completed in milliseconds. The configured per-asset maximum is only a soft diagnostic threshold, and all events carrying open-order IDs remain nonreplaceable on one global worker that also performs position-mark work. Draft contract `RC-20260823-maker-evidence-fast-lane` validated at `sha256:dd8e0fb72f6ea8619229439d6820d6347cf6d0d218a6821fa62074cb0ff0fee1`; no product or production mutation was made pending explicit approval.

## 2026-08-23T05:57:03.613908Z
Request:
Проверь сервер и ставки

Result:
Read-only production verification confirmed build `a28aff1d...` Running/Live with fresh heartbeat/reference data, connected current WebSockets, 3,105 Filled Paper orders across BTC/ETH/SOL in the fixed last hour, no active overdue runs, no expired open orders, and no persistent PostgreSQL locks. Copied-trader performance projection recovered without restart and had zero inflight rows; its last stream-read error was `05:41:26.114543Z`. Exact ETH Maker-GTD remained degraded: 28 fills versus 27 timeout-expirations in the hour, with persisted side-effect queue snapshots up to 157,760 global and 66,044 matching updates. Sixteen Live orders in the last 24 hours were all Matched. No production or product mutation was performed.

## 2026-08-23T07:04:26.5924843Z
Request:
APPROVE RC-20260823-maker-evidence-fast-lane sha256:dd8e0fb72f6ea8619229439d6820d6347cf6d0d218a6821fa62074cb0ff0fee1

Result:
Implemented the approved exact ETH Maker-GTD evidence fast lane without changing strategy, pricing, stake, TouchNoDepth, accounting, mandatory label, PaperOnly, or Live behavior. The dedicated lane retains every eligible event FIFO with original timestamps, fingerprint, source id, and raw payload, remains independent of blocked general position-mark work, participates in expiry drain, and exposes separate pending/in-flight telemetry. General quotes coalesce only after Maker IDs are separated and no ordinary Paper order needs the event. Final tests passed 81/81 focused and 115/115 contract/WebSocket; the 160,000-event stress preserved exact FIFO/fingerprint/RawJson evidence. Full Debug solution build passed with 0 errors and 126 pre-existing warnings, none in changed files. Independent semantic review passed with no open findings. No production, database, service, configuration, schema, migration, subscription, or deployment state was changed.

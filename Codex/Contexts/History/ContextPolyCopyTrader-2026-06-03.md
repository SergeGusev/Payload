# ContextPolyCopyTrader History 2026-06-03

## 2026-06-03T00:08:12.6752745+03:00
Request:
User deployed and asked to verify production after the separate Live condition skip boundary change.

Result:
Verified production read-only via `out\dbprobe` with a temporary host override to `192.168.0.101` and without printing the connection string. `PolyCopyTrader.Service` is running in `Live` mode on service-code commit `ad88ae97f6411b30cd1130ca21f2d29b629aa5e1`, started `2026-06-02T20:58:11Z`, with fresh heartbeat and empty `last_error`. The `strategies.live_enabled_at_utc` schema column exists; production has `3` Live strategies, all `3` have non-null Live boundaries, and `0` non-Live strategies have stale boundaries. BTC Middle 47, ETH Skip 7, and SOL Skip 42 are all effective-live with `auto_live_paused=false`. The Live condition skip boundary is active: BTC old all-paper condition skip `2530` vs Live condition skip `97`; ETH `1040` vs `47`; SOL `2462` vs `2`. Recent API errors in the last 15 minutes are `0`. No production writes or source code changes were performed in the verification step.

## 2026-06-03T06:45:05.4936965Z
Request:
Что лучше делать с точки зрения ликвидности ордера, то есть чтобы он легче находил встречное предложение, еслия хочу увеличит ставку вдвое. Делать одни ставку вдвое больше или делать пакетную передачу двух ордеров на одинаковую сумму?

Result:
Answered using current official Polymarket documentation. For same side, same token, same limit price, and ordinary passive GTC/GTD placement, two equal orders do not create extra available liquidity or make matching easier versus one doubled order; they still face the same order book and price/time priority. Recommended one doubled order for simpler accounting and cancellation unless there is a specific need for independent slices. For immediate execution, recommended using order book depth and FAK/partial-fill behavior when partial execution is acceptable; two smaller FOK orders are only useful when each slice must be all-or-nothing and one slice may fit available depth while a single doubled FOK would fail. No source code or production state changed.

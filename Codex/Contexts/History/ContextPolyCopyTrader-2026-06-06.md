## 2026-06-05T21:11:52.9292887Z
Request:
Проверь сейчас

Result:
Checked production PostgreSQL read-only after global Auto Live Pause disablement. Production DB currently has `0` `auto_live_paused` rows and `0` pause-anchor rows. All six manually Live-enabled fixed `Up/Down 50 bps Instant` strategies have `live_stakes=true`, `auto_live_paused=false`, and `effective_live_stakes=true`. `PolyCopyTrader.Service` is now running commit `d51a8f9` in Live mode, started `2026-06-05T20:43:53Z`, heartbeat fresh at `2026-06-05T21:10:26Z`, with `last_error=null`. Repository config has `AutoLivePauseStrategies: []`. One Live order was created after disablement (`BTC Up or Down 5m Down 50 bps Instant`, created `2026-06-05T20:51:37Z`, settled `2026-06-05T20:57:21Z`, realized PnL `-1.417800`), and Auto Live Pause remained disabled.

## 2026-06-06T08:06:05.5147027Z
Request:
Что ты думаешь о текущих Live стратегиях? Насколько они перспективны?

Result:
Assessed current Live-enabled fixed `Up/Down 50 bps Instant` strategies from production data read-only. The service is running commit `d51a8f9` in Live mode with fresh heartbeat and `last_error=null`; Auto Live Pause remains disabled with `0` paused/anchor rows. Overall Live since each strategy's `live_enabled_at_utc`: `80` orders, `59` settled, `29W/30L`, cost basis `168.807177`, PnL `+23.383723`, ROI `13.8523%`; status mix `59` Matched, `10` PreflightRejected, `8` CancelFailed, `3` Rejected. Same-strategy Paper over the same period: `94` settled, `50W/44L`, PnL `+51.328723`, ROI `18.4052%`. After Auto Live Pause disablement: `46` Live orders, `33` settled, `19W/14L`, PnL `+30.845526`, ROI `32.0345%`. Strongest current Live candidates are ETH Down and SOL Up; SOL Down is moderately positive; BTC Up is marginal; ETH Up and BTC Down remain caution/weak, especially BTC Down. Recommended keeping stake small and monitoring until larger per-strategy Live samples accumulate.

## 2026-06-05T21:11:52.9292887Z
Request:
Проверь сейчас

Result:
Checked production PostgreSQL read-only after global Auto Live Pause disablement. Production DB currently has `0` `auto_live_paused` rows and `0` pause-anchor rows. All six manually Live-enabled fixed `Up/Down 50 bps Instant` strategies have `live_stakes=true`, `auto_live_paused=false`, and `effective_live_stakes=true`. `PolyCopyTrader.Service` is now running commit `d51a8f9` in Live mode, started `2026-06-05T20:43:53Z`, heartbeat fresh at `2026-06-05T21:10:26Z`, with `last_error=null`. Repository config has `AutoLivePauseStrategies: []`. One Live order was created after disablement (`BTC Up or Down 5m Down 50 bps Instant`, created `2026-06-05T20:51:37Z`, settled `2026-06-05T20:57:21Z`, realized PnL `-1.417800`), and Auto Live Pause remained disabled.

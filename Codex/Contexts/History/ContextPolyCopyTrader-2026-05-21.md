## 2026-05-20T21:19:38.4496811+00:00
Request:
Выложил, проверяй

Result:
Verified production after deploy read-only through remote PostgreSQL host `192.168.0.101`. `PolyCopyTrader.Service` is `Running` in `Live` mode on build `info=1.0.0+e547fcd535f2ecbbeafd7259635eda26e5503d0b`, restarted at `2026-05-20T21:13:13Z`, and has fresh heartbeat with empty `last_error`. The new Maker strategy rows exist and are enabled with `live_stakes=false`; Maker runs/orders are `0` as expected because production is in `Live` mode and the strategies are Paper-only. Normal BTC Paper processing continued after restart, Live orders since restart were `0`, Paper/Live-shadow discrepancies for 24h were `0`, market WebSocket recovered to `Connected`, and no API errors appeared after `2026-05-20T21:15:10Z`. No DB writes, service restart, live order submission, or cancel action was performed.

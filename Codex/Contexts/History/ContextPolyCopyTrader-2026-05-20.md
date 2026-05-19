## 2026-05-19T21:13:03.1417354Z
Request:
Почему BTC Up or Down 5m Binance 1.9 bps весь в плюсах, а BTC Up or Down 5m Binance 1.8 bps в таких серьёзных минусах? Вроде как почти рядом

Result:
Queried production PostgreSQL read-only. Confirmed `btc_up_down_5m_binance_bps_1_8` is Paper-only with `20` settled runs, `8/12` W/L, `-12.0000` PnL, while `btc_up_down_5m_binance_bps_1_9` is LiveStakes-enabled with `47` Paper/shadow settled, `28/19` W/L, `+27.4862` Paper/shadow PnL, and actual live `+18.4862` over `38` settled live orders. The main divergence is not the `0.1 bps` threshold gap: after `1.9` went live at `2026-05-18T19:00Z`, `1.9` used `paper_live_shadow_test`/actual live fills, while `1.8` remained ordinary Paper GTD at `0.50`; `32` markets settled for `1.9` but were `gtd_limit_not_filled` for `1.8`, producing `+35.4699` for `1.9`. Before live, both shared `9` settled trades for `+9.0000` each and `1.8`'s extra marginal trades netted `0.0000`. No DB writes, code changes, service restart, live order submission, or cancel action was performed.

## 2026-05-19T21:20:22.7122831Z
Request:
Давай BTC Up or Down 5m Binance 2.3 bps переведём в Live

Result:
Added `btc_up_down_5m_binance_bps_2_3` to the BTC Paper/Live-shadow allowlist, added a regression test proving `2.3 bps` creates a Paper-shadow plus GTD Live order with `postOnly=false`, updated the live-stakes admin test to include exactly `1.9`, `2.0`, `2.0 Instant`, `2.1`, `2.2`, and `2.3`, and updated README live safety docs. Targeted tests passed 7/7 and full tests passed 504/504. Updated production PostgreSQL LiveStakes on host `192.168.0.101` to enable exactly the six BTC strategies above; verification showed `6` live-enabled strategies, ETH/SOL live count `0`, `2.3 bps` enabled with `liveStake=1`, `liveBalance=100`, `liveOpen=0`, `liveOrders=0`, and DB open live orders `0`. Production service is still running with fresh heartbeat on old build `info=1.0.0+1a04041365619cc35eb5c91ac2c96255e13ffa41`; deploy/restart from the new commit is still required before `2.3 bps` can live-place orders. No manual live order submission, cancel action, or service restart was performed.

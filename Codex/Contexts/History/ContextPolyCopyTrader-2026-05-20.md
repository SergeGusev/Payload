## 2026-05-19T21:13:03.1417354Z
Request:
Почему BTC Up or Down 5m Binance 1.9 bps весь в плюсах, а BTC Up or Down 5m Binance 1.8 bps в таких серьёзных минусах? Вроде как почти рядом

Result:
Queried production PostgreSQL read-only. Confirmed `btc_up_down_5m_binance_bps_1_8` is Paper-only with `20` settled runs, `8/12` W/L, `-12.0000` PnL, while `btc_up_down_5m_binance_bps_1_9` is LiveStakes-enabled with `47` Paper/shadow settled, `28/19` W/L, `+27.4862` Paper/shadow PnL, and actual live `+18.4862` over `38` settled live orders. The main divergence is not the `0.1 bps` threshold gap: after `1.9` went live at `2026-05-18T19:00Z`, `1.9` used `paper_live_shadow_test`/actual live fills, while `1.8` remained ordinary Paper GTD at `0.50`; `32` markets settled for `1.9` but were `gtd_limit_not_filled` for `1.8`, producing `+35.4699` for `1.9`. Before live, both shared `9` settled trades for `+9.0000` each and `1.8`'s extra marginal trades netted `0.0000`. No DB writes, code changes, service restart, live order submission, or cancel action was performed.

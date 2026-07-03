## 2026-07-03T05:31:00.1018774Z
Request:
Смотрю статистику за сутки по стратегии SOL Up or Down 5m Down 8 bps Reference Average Premarket. 99 побед против 95 поражений. При этом в Paper показывает Pnl + 0.5, а в Live Pnl - 8.

Откуда такое расхождение?

Result:
Investigated read-only on server database `192.168.0.101` / `polycopytrader`. The strategy is `sol_up_down_5m_down_bps_8_fak_premarket` (`b7c50005-0000-4000-8139-000000000108`), currently `enabled=true`, `live_stakes=true`, `Live $=1`, `Live bal` about `$53.20`. The latest raw 24h query had one additional settled loss compared with the user's view: `99/96`, Paper `-$2.44`, Live `-$14.77`; excluding that latest loss gives the user's observed shape, about Paper `+$0.54` and Live `-$8.77` at `99/95`. All live-settled rows matched Paper rows by `paper_order_id`, so the discrepancy is not missing wins. The cause is exposure/notional mismatch: Paper stake summed about `$603.37` while Live cost basis summed about `$1170.03` over the matched rows; average Paper stake was about `$3.09` versus average Live cost about `$6.00`. Paper uses about `6.07` shares per row from worst-price sizing at `0.99`, while the Live market-buy amount fills about `11.67` shares on average at actual prices around `0.518`. Conclusion: FAK live-shadow Paper accounting and Live amount-order accounting are not directly comparable for this strategy; Paper PnL under-represents the actual Live exposure. No production writes, Live changes, orders, restarts, source-code changes, build, or tests were performed.

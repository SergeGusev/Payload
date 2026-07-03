## 2026-07-03T05:31:00.1018774Z
Request:
Смотрю статистику за сутки по стратегии SOL Up or Down 5m Down 8 bps Reference Average Premarket. 99 побед против 95 поражений. При этом в Paper показывает Pnl + 0.5, а в Live Pnl - 8.

Откуда такое расхождение?

Result:
Investigated read-only on server database `192.168.0.101` / `polycopytrader`. The strategy is `sol_up_down_5m_down_bps_8_fak_premarket` (`b7c50005-0000-4000-8139-000000000108`), currently `enabled=true`, `live_stakes=true`, `Live $=1`, `Live bal` about `$53.20`. The latest raw 24h query had one additional settled loss compared with the user's view: `99/96`, Paper `-$2.44`, Live `-$14.77`; excluding that latest loss gives the user's observed shape, about Paper `+$0.54` and Live `-$8.77` at `99/95`. All live-settled rows matched Paper rows by `paper_order_id`, so the discrepancy is not missing wins. The cause is exposure/notional mismatch: Paper stake summed about `$603.37` while Live cost basis summed about `$1170.03` over the matched rows; average Paper stake was about `$3.09` versus average Live cost about `$6.00`. Paper uses about `6.07` shares per row from worst-price sizing at `0.99`, while the Live market-buy amount fills about `11.67` shares on average at actual prices around `0.518`. Conclusion: FAK live-shadow Paper accounting and Live amount-order accounting are not directly comparable for this strategy; Paper PnL under-represents the actual Live exposure. No production writes, Live changes, orders, restarts, source-code changes, build, or tests were performed.
## 2026-07-03T05:49:51.0043116Z
Request:
Выравнять надо обязательно. Но ведь это означает, что и на накопленную в Paper статистику я не могу ориентироваться? Как сделать так, чтобы она копилась со 100% достоверностью?

Result:
Explained and implemented the required alignment for future FAK paper-live-shadow rows: old accumulated Paper rows for FAK Live-shadow strategies are not exact Live history because they were simulated separately from the actual Live amount-order fill. Updated `BtcUpDown5mPaperStrategyProcessor` so matched FAK live-shadow entries copy actual Live average fill price, filled size, and cost basis into the Paper order, Paper fill, and strategy run, with raw diagnostics marked `paper_fak_fill_model=live_order_actual_fill_v1`. Added/updated ETH FAK live-shadow regression tests for regular and premarket paths; targeted verification passed `2/2`. Full test project still has broad existing strategy-test failures (`78` failed, `711` passed), so only the focused regression was clean. No production deployment, database backfill, Live enablement change, order submission, cancel action, or service restart was performed.

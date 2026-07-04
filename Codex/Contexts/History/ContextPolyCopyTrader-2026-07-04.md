## 2026-07-04T10:44:18.0882100+00:00
Request:
Добавь в Dashboard галочку "Hide progress" во все табы. При нажатой галочке будут прятаться все стратегии, у которых в названии есть слово Progress

Result:
Added `Hide progress` checkboxes to the Dashboard Strategies `All`, `24 hours`, `6 hours`, and `1 hour` tabs. Each checkbox hides strategy rows whose visible name contains `Progress`, case-insensitively; the `All` tab setting also applies to its nested recent-performance grid. Updated README and configuration reference documentation. Verification passed: Dashboard build with a temporary output directory succeeded, and `git diff --check` reported no whitespace errors, only existing line-ending warnings. No production DB writes, order submissions, cancels, service restarts, or Live setting changes were performed.

## 2026-07-04T11:02:30.6916383+00:00
Request:
Почему выигрывшая живая ставка по ETH Up or Down 5m Down 6 bps Reference Average Premarket принесла так мало денег?

Result:
Queried production PostgreSQL read-only at `192.168.0.101`. The winning Live order `29bc3286-462b-4717-8e1a-818069e52c31` was recorded as `6.07` shares at `0.99`, cost `6.0093`, settlement value `6.07`, and realized PnL `0.0607`. In the same ETH 6:40-6:45 market, five Live orders bought `Up`: three direct CLOB `matched` responses recorded true fill prices around `0.52` and PnL around `$5.3-$5.6`, while `Down 1` and `Down 6` lacked direct CLOB making/taking amounts and were reconciled from the aggregate Data API position, causing the code to fall back to worst price `0.99`. Paper shadow for `Down 6` saw `best_ask=0.51` and simulated about `$6` filled at average `0.51`; aggregate Data API size matched the five-order paper expected shares. Conclusion: the small displayed profit is a Live reconciliation/accounting artifact, not evidence that the real fill was near `0.99`. No production writes, source changes, service restarts, order submissions, or cancels were performed.

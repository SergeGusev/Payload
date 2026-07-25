## 2026-07-25T07:19:10.0737508Z
Request:
Сделай Pnl график стратегии ETH Up or Down 5m 3 bps Reference Average Premarket с наложенным курсом ETH

Result:
Generated and visually inspected `outputs/019f88ae-b840-74e1-9392-4f7b2ef076c0/eth-3-reference-average-pnl-vs-eth-20260725-101831/eth-3-reference-average-pnl-vs-eth.png` from a read-only production PostgreSQL snapshot through `2026-07-25T07:19:05.061799Z`. The exact strategy had 5,112 settled Paper rows, PnL `+$533.28540032`, ROI `+1.73598098%`; independent Paper and ETHUSDT minute-series aggregate checks passed. The overlaid ETHUSDT series covered 29,597 of 29,802 expected UTC minutes (`99.312127%`) and moved `+$4.97181976%`.
## 2026-07-25T07:25:00.0000000Z
Request:
Получается, что эта стратегия растёт только когда растёт ETH ?

Result:
Checked the verified chart CSVs by UTC day. Daily ETH change vs daily Paper PnL correlation was `0.7239`: ETH rose on 13 days and the strategy was positive on 12 of them for `+$1,012.05202710`; ETH fell on 9 days and the strategy was negative on 7 of them for `-$478.76662678`. The relationship is strong but not absolute because `2026-07-11` and `2026-07-25` had ETH down with positive PnL, while `2026-07-22` had ETH up with negative PnL.
## 2026-07-25T07:30:00.0000000Z
Request:
А объясни, почему так происходит. Ведь эта стратегия работает не только на Up, но и на Down

Result:
Explained from verified code and production aggregates that the neutral Reference Average strategy can buy both outcomes, but it is not balanced in realized trading. The code buys `Down` only when current ETH is above the largest full reference average by the threshold, and buys `Up` when current ETH is below that largest reference average by the threshold. Through the chart cutoff, production settled rows were 4,432 `Up` (`86.70%`) for `+$267.53745395` and 680 `Down` (`13.30%`) for `+$265.74794637`, so the high-frequency exposure is mostly Up even though Down trades also contribute materially.

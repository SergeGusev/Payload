# Diff Shift Progress Backtest

Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.
Model: six strategies, BTC/ETH/SOL x Up/Down. The Up side uses `Diff = UpCount - DownCount`; the Down side uses `Diff = DownCount - UpCount`. Counters and `Sum` are continuous after the first full UTC day. Before each market, the previous candle result settles the pending bet, updates counts, applies `while Sum > Unit && Diff > 1` shift, and then enters only when `Diff > 0`. Stake multiplier is `Diff + 1`.
PnL model: fixed 0.50 binary odds, `Unit = 1`, so a settled winning stake earns `+stake` and a losing stake earns `-stake`. Binance `Flat` candles close pending entries with zero PnL and do not change Up/Down counts.
The first partial UTC day in the six-month CSV is skipped per asset so counters start at a clean midnight.

## Asset Summary

| Asset | Strategies | Markets | Entries | Settled | Flat | PnL units | ROI | Max DD | Min equity | Shifts | Max mult | Best | Worst |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| BTC | 2 | 52230 | 842 | 840 | 2 | 260 | 10.52% | 67 | -19 | 243 | 12 | BTC Up or Down 5m Diff Up Shift Progress (136) | BTC Up or Down 5m Diff Down Shift Progress (124) |
| ETH | 2 | 52230 | 1483 | 1477 | 6 | 306 | 3.06% | 197 | -142 | 291 | 22 | ETH Up or Down 5m Diff Up Shift Progress (255) | ETH Up or Down 5m Diff Down Shift Progress (51) |
| SOL | 2 | 52230 | 921 | 905 | 16 | 219 | 4.31% | 272 | -87 | 202 | 25 | SOL Up or Down 5m Diff Down Shift Progress (127) | SOL Up or Down 5m Diff Up Shift Progress (92) |
| ALL | 6 | 156690 | 3246 | 3222 | 24 | 785 | 4.47% | 317 | -74 | 736 | 25 | ETH Up or Down 5m Diff Up Shift Progress (255) | ETH Up or Down 5m Diff Down Shift Progress (51) |

## Strategies

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Min equity | Shifts | Final Diff | Final Sum |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m Diff Down Shift Progress | 381 | 381 | 55.12% | 1174 | 124 | 10.56% | 67 | -23 | 116 | -56 | 8 |
| BTC Up or Down 5m Diff Up Shift Progress | 461 | 459 | 52.72% | 1298 | 136 | 10.48% | 35 | -24 | 127 | -187 | 9 |
| ETH Up or Down 5m Diff Down Shift Progress | 173 | 173 | 52.02% | 741 | 51 | 6.88% | 84 | -66 | 40 | -38 | 11 |
| ETH Up or Down 5m Diff Up Shift Progress | 1310 | 1304 | 50% | 9273 | 255 | 2.75% | 197 | -153 | 251 | -253 | 4 |
| SOL Up or Down 5m Diff Down Shift Progress | 614 | 606 | 50.17% | 3951 | 127 | 3.21% | 272 | -167 | 120 | -64 | 7 |
| SOL Up or Down 5m Diff Up Shift Progress | 307 | 299 | 54.18% | 1130 | 92 | 8.14% | 78 | -45 | 82 | -138 | 10 |

## Window

- BTC: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- ETH: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- SOL: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.

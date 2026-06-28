# Uncapped Diff Progress Backtest

Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.
Model: evaluate each 5m market using the previous resolved 5m outcomes since `00:00 UTC`; reset counters at every UTC day boundary in both waiting and betting modes; enter when side-specific `Diff > N`; buy the opposite outcome; stake multiplier is uncapped `Diff - N`.
PnL model: fixed 0.50 binary odds, so a settled winning stake earns `+stake` and a losing stake earns `-stake`; Binance `Flat` candles are counted as `flat_entries` and excluded from settled PnL/ROI because they do not provide an Up/Down result.
The first partial UTC day in the six-month CSV is skipped per asset so daily counters start from midnight.
The `above_10_multiplier_entries` metric counts entries that would have been capped by the previous max multiplier of 10.

## Asset Summary

| Asset | Markets | Entries | Settled | Flat | PnL units | ROI | Best PnL strategy | Worst PnL strategy |
|---|---:|---:|---:|---:|---:|---:|---|---|
| BTC | 52230 | 384303 | 383444 | 859 | 23789 | 0.8% | BTC Up or Down 5m 1 Diff Down Progress (1776) | BTC Up or Down 5m 47 Diff Up Progress (0) |
| ETH | 52230 | 347435 | 346693 | 742 | 61709 | 2.47% | ETH Up or Down 5m 1 Diff Down Progress (3739) | ETH Up or Down 5m 39 Diff Up Progress (0) |
| SOL | 52230 | 339761 | 328137 | 11624 | 62731 | 2.79% | SOL Up or Down 5m 1 Diff Down Progress (3875) | SOL Up or Down 5m 40 Diff Up Progress (0) |

## Top Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Above10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m 1 Diff Down Progress | 23600 | 22728 | 50.72% | 174883 | 3875 | 2.22% | 906 | 6135 |
| ETH Up or Down 5m 1 Diff Down Progress | 22601 | 22555 | 50.63% | 181841 | 3739 | 2.06% | 1001 | 6510 |
| SOL Up or Down 5m 2 Diff Down Progress | 21086 | 20303 | 50.86% | 152155 | 3547 | 2.33% | 854 | 5226 |
| ETH Up or Down 5m 2 Diff Down Progress | 20276 | 20238 | 50.55% | 159286 | 3454 | 2.17% | 925 | 5624 |
| SOL Up or Down 5m 1 Diff Up Progress | 20771 | 19993 | 50.67% | 153254 | 3396 | 2.22% | 1018 | 5223 |
| ETH Up or Down 5m 3 Diff Down Progress | 18161 | 18129 | 50.66% | 139048 | 3230 | 2.32% | 856 | 4825 |
| SOL Up or Down 5m 3 Diff Down Progress | 18723 | 18039 | 50.93% | 131852 | 3198 | 2.43% | 800 | 4402 |
| SOL Up or Down 5m 2 Diff Up Progress | 18529 | 17847 | 50.64% | 133261 | 3127 | 2.35% | 964 | 4383 |
| ETH Up or Down 5m 4 Diff Down Progress | 16130 | 16102 | 50.94% | 120919 | 2989 | 2.47% | 783 | 4132 |
| SOL Up or Down 5m 3 Diff Up Progress | 16464 | 15881 | 50.78% | 115414 | 2900 | 2.51% | 909 | 3650 |
| SOL Up or Down 5m 4 Diff Down Progress | 16522 | 15916 | 51.14% | 113813 | 2863 | 2.52% | 742 | 3736 |
| ETH Up or Down 5m 5 Diff Down Progress | 14225 | 14202 | 50.91% | 104817 | 2687 | 2.56% | 722 | 3505 |
| ETH Up or Down 5m 1 Diff Up Progress | 21321 | 21259 | 50.94% | 164852 | 2652 | 1.61% | 1307 | 5328 |
| SOL Up or Down 5m 4 Diff Up Progress | 14529 | 14030 | 50.93% | 99533 | 2651 | 2.66% | 856 | 3035 |
| SOL Up or Down 5m 5 Diff Down Progress | 14487 | 13967 | 51.08% | 97897 | 2499 | 2.55% | 681 | 3197 |

## Top Strategies By ROI (min 50 settled bets)

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Above10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m 36 Diff Up Progress | 54 | 54 | 66.67% | 88 | 36 | 40.91% | 6 | 0 |
| SOL Up or Down 5m 35 Diff Up Progress | 86 | 86 | 58.14% | 174 | 50 | 28.74% | 10 | 0 |
| ETH Up or Down 5m 33 Diff Up Progress | 58 | 58 | 58.62% | 134 | 34 | 25.37% | 13 | 0 |
| SOL Up or Down 5m 34 Diff Up Progress | 113 | 112 | 55.36% | 286 | 62 | 21.68% | 15 | 0 |
| ETH Up or Down 5m 32 Diff Up Progress | 83 | 83 | 57.83% | 217 | 47 | 21.66% | 19 | 0 |
| ETH Up or Down 5m 30 Diff Up Progress | 198 | 198 | 61.11% | 539 | 115 | 21.34% | 33 | 0 |
| ETH Up or Down 5m 31 Diff Up Progress | 124 | 124 | 59.68% | 341 | 71 | 20.82% | 25 | 0 |
| ETH Up or Down 5m 29 Diff Up Progress | 309 | 308 | 59.09% | 847 | 171 | 20.19% | 41 | 0 |
| SOL Up or Down 5m 33 Diff Up Progress | 140 | 138 | 55.07% | 424 | 76 | 17.92% | 21 | 0 |
| ETH Up or Down 5m 28 Diff Up Progress | 434 | 433 | 55.89% | 1280 | 222 | 17.34% | 51 | 1 |
| ETH Up or Down 5m 33 Diff Down Progress | 56 | 56 | 51.79% | 154 | 26 | 16.88% | 14 | 0 |
| ETH Up or Down 5m 30 Diff Down Progress | 144 | 144 | 62.5% | 452 | 74 | 16.37% | 31 | 0 |
| SOL Up or Down 5m 31 Diff Down Progress | 73 | 72 | 58.33% | 203 | 33 | 16.26% | 43 | 0 |
| ETH Up or Down 5m 29 Diff Down Progress | 229 | 229 | 57.21% | 681 | 107 | 15.71% | 40 | 0 |
| SOL Up or Down 5m 30 Diff Down Progress | 113 | 112 | 56.25% | 315 | 47 | 14.92% | 52 | 1 |

## Worst Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Above10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m 47 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| BTC Up or Down 5m 48 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| BTC Up or Down 5m 49 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| BTC Up or Down 5m 50 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| BTC Up or Down 5m 50 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 39 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 39 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 40 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 40 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 41 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 41 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 42 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 42 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 43 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| ETH Up or Down 5m 43 Diff Down Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |

## Window

- BTC: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- ETH: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- SOL: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.

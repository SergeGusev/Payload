# Diff Progress Backtest

Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.
Model: evaluate each 5m market using the previous resolved 5m outcomes since `00:00 UTC`; reset counters at every UTC day boundary in both waiting and betting modes; enter when side-specific `Diff > N`; buy the opposite outcome; stake multiplier is `min(Diff - N, 10)`.
PnL model: fixed 0.50 binary odds, so a settled winning stake earns `+stake` and a losing stake earns `-stake`; Binance `Flat` candles are counted as `flat_entries` and excluded from settled PnL/ROI because they do not provide an Up/Down result.
The first partial UTC day in the six-month CSV is skipped per asset so daily counters start from midnight.

## Asset Summary

| Asset | Markets | Entries | Settled | Flat | PnL units | ROI | Best PnL strategy | Worst PnL strategy |
|---|---:|---:|---:|---:|---:|---:|---|---|
| BTC | 52230 | 384303 | 383444 | 859 | 16849 | 0.74% | BTC Up or Down 5m 5 Diff Down Progress (1213) | BTC Up or Down 5m 11 Diff Up Progress (-66) |
| ETH | 52230 | 347435 | 346693 | 742 | 40626 | 2.03% | ETH Up or Down 5m 1 Diff Down Progress (2167) | ETH Up or Down 5m 39 Diff Up Progress (0) |
| SOL | 52230 | 339761 | 328137 | 11624 | 47400 | 2.57% | SOL Up or Down 5m 1 Diff Down Progress (2834) | SOL Up or Down 5m 40 Diff Up Progress (0) |

## Top Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m 1 Diff Down Progress | 23600 | 22728 | 50.72% | 137952 | 2834 | 2.05% | 598 | 6135 |
| SOL Up or Down 5m 2 Diff Down Progress | 21086 | 20303 | 50.86% | 121135 | 2651 | 2.19% | 542 | 5226 |
| SOL Up or Down 5m 3 Diff Down Progress | 18723 | 18039 | 50.93% | 105865 | 2463 | 2.33% | 501 | 4402 |
| SOL Up or Down 5m 1 Diff Up Progress | 20771 | 19993 | 50.67% | 121297 | 2289 | 1.89% | 518 | 5223 |
| SOL Up or Down 5m 4 Diff Down Progress | 16522 | 15916 | 51.14% | 92078 | 2248 | 2.44% | 474 | 3736 |
| SOL Up or Down 5m 2 Diff Up Progress | 18529 | 17847 | 50.64% | 106358 | 2198 | 2.07% | 489 | 4383 |
| ETH Up or Down 5m 1 Diff Down Progress | 22601 | 22555 | 50.63% | 140669 | 2167 | 1.54% | 732 | 6510 |
| SOL Up or Down 5m 3 Diff Up Progress | 16464 | 15881 | 50.78% | 92758 | 2134 | 2.3% | 473 | 3650 |
| ETH Up or Down 5m 2 Diff Down Progress | 20276 | 20238 | 50.55% | 124617 | 2039 | 1.64% | 667 | 5624 |
| SOL Up or Down 5m 4 Diff Up Progress | 14529 | 14030 | 50.93% | 80413 | 2031 | 2.53% | 463 | 3035 |
| ETH Up or Down 5m 1 Diff Up Progress | 21321 | 21259 | 50.94% | 124997 | 2027 | 1.62% | 954 | 5328 |
| SOL Up or Down 5m 5 Diff Down Progress | 14487 | 13967 | 51.08% | 79768 | 1972 | 2.47% | 443 | 3197 |
| ETH Up or Down 5m 3 Diff Down Progress | 18161 | 18129 | 50.66% | 109996 | 1960 | 1.78% | 585 | 4825 |
| SOL Up or Down 5m 5 Diff Up Progress | 12765 | 12345 | 50.9% | 69329 | 1869 | 2.7% | 453 | 2561 |
| ETH Up or Down 5m 4 Diff Down Progress | 16130 | 16102 | 50.94% | 96686 | 1864 | 1.93% | 498 | 4132 |

## Top Strategies By ROI (min 50 settled bets)

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
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
| ETH Up or Down 5m 28 Diff Up Progress | 434 | 433 | 55.89% | 1279 | 221 | 17.28% | 51 | 1 |
| ETH Up or Down 5m 33 Diff Down Progress | 56 | 56 | 51.79% | 154 | 26 | 16.88% | 14 | 0 |
| ETH Up or Down 5m 30 Diff Down Progress | 144 | 144 | 62.5% | 452 | 74 | 16.37% | 31 | 0 |
| SOL Up or Down 5m 31 Diff Down Progress | 73 | 72 | 58.33% | 203 | 33 | 16.26% | 43 | 0 |
| ETH Up or Down 5m 29 Diff Down Progress | 229 | 229 | 57.21% | 681 | 107 | 15.71% | 40 | 0 |
| SOL Up or Down 5m 30 Diff Down Progress | 113 | 112 | 56.25% | 314 | 46 | 14.65% | 52 | 1 |

## Worst Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m 11 Diff Up Progress | 5926 | 5911 | 49.94% | 34662 | -66 | -0.19% | 593 | 1475 |
| BTC Up or Down 5m 16 Diff Up Progress | 3002 | 2996 | 49.63% | 17258 | -58 | -0.34% | 465 | 733 |
| BTC Up or Down 5m 13 Diff Up Progress | 4528 | 4518 | 49.93% | 26305 | -49 | -0.19% | 554 | 1087 |
| BTC Up or Down 5m 17 Diff Up Progress | 2635 | 2630 | 49.58% | 14994 | -48 | -0.32% | 444 | 648 |
| BTC Up or Down 5m 15 Diff Up Progress | 3440 | 3431 | 49.93% | 19866 | -46 | -0.23% | 493 | 824 |
| BTC Up or Down 5m 14 Diff Up Progress | 3952 | 3942 | 49.92% | 22871 | -45 | -0.2% | 521 | 939 |
| BTC Up or Down 5m 12 Diff Up Progress | 5189 | 5176 | 50.04% | 30223 | -43 | -0.14% | 582 | 1261 |
| BTC Up or Down 5m 18 Diff Up Progress | 2313 | 2309 | 49.63% | 13011 | -33 | -0.25% | 421 | 568 |
| BTC Up or Down 5m 10 Diff Up Progress | 6793 | 6775 | 50.36% | 39703 | -33 | -0.08% | 604 | 1738 |
| BTC Up or Down 5m 19 Diff Up Progress | 2019 | 2015 | 49.98% | 11269 | -17 | -0.15% | 394 | 491 |
| BTC Up or Down 5m 20 Diff Up Progress | 1738 | 1734 | 50.46% | 9744 | -10 | -0.1% | 356 | 409 |
| BTC Up or Down 5m 21 Diff Up Progress | 1475 | 1472 | 50.54% | 8418 | -8 | -0.1% | 325 | 324 |
| BTC Up or Down 5m 22 Diff Up Progress | 1261 | 1258 | 49.92% | 7269 | -5 | -0.07% | 293 | 256 |
| BTC Up or Down 5m 47 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |
| BTC Up or Down 5m 48 Diff Up Progress | 0 | 0 | 0% | 0 | 0 | 0% | 0 | 0 |

## Window

- BTC: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- ETH: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- SOL: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.

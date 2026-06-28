# Diff Progress Waiting-Reset-Zero Backtest

Source: `outputs/binance-diff-time-chart-2026-06-28/binance-diff-timeseries.csv`.
Model: each strategy owns its own counters because reset behavior depends on that strategy's mode; evaluate each 5m market using previous resolved outcomes; while `Waiting`, reset counters at every UTC day boundary; while `Betting`, carry counters across midnight and keep betting until side-specific `Diff <= 0`; enter when side-specific `Diff > N`; buy the opposite outcome; stake multiplier is `min(max(Diff - N, 1), 10)` so bets continue below `N` down to zero.
PnL model: fixed 0.50 binary odds, so a settled winning stake earns `+stake` and a losing stake earns `-stake`; Binance `Flat` candles are counted as `flat_entries` and excluded from settled PnL/ROI because they do not provide an Up/Down result.
The first partial UTC day in the six-month CSV is skipped per asset so initial waiting-mode counters start from midnight.

## Asset Summary

| Asset | Markets | Open@end | Entries | Settled | Flat | PnL units | ROI | Best PnL strategy | Worst PnL strategy |
|---|---:|---:|---:|---:|---:|---:|---:|---|---|
| BTC | 52230 | 83 | 3427022 | 3421397 | 5625 | 432 | 0% | BTC Up or Down 5m 37 Diff Up Progress (1349) | BTC Up or Down 5m 5 Diff Down Progress (-1062) |
| ETH | 52230 | 58 | 2965329 | 2958019 | 7310 | 310 | 0% | ETH Up or Down 5m 14 Diff Up Progress (1315) | ETH Up or Down 5m 4 Diff Down Progress (-1151) |
| SOL | 52230 | 66 | 2791838 | 2685214 | 106624 | 39790 | 0.16% | SOL Up or Down 5m 19 Diff Up Progress (2423) | SOL Up or Down 5m 36 Diff Down Progress (-806) |

## Top Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m 19 Diff Up Progress | 45412 | 43650 | 50.12% | 390101 | 2423 | 0.62% | 1492 | 37219 |
| SOL Up or Down 5m 18 Diff Up Progress | 45443 | 43681 | 50.12% | 394840 | 2350 | 0.6% | 1506 | 37755 |
| SOL Up or Down 5m 17 Diff Up Progress | 45526 | 43762 | 50.11% | 399496 | 2258 | 0.57% | 1515 | 38303 |
| SOL Up or Down 5m 16 Diff Up Progress | 45546 | 43781 | 50.1% | 403908 | 2136 | 0.53% | 1523 | 38895 |
| SOL Up or Down 5m 15 Diff Up Progress | 45819 | 44048 | 50.11% | 408359 | 2033 | 0.5% | 1528 | 39516 |
| SOL Up or Down 5m 20 Diff Up Progress | 36260 | 34833 | 50.1% | 309606 | 1960 | 0.63% | 1479 | 29539 |
| SOL Up or Down 5m 14 Diff Up Progress | 45880 | 44108 | 50.1% | 412350 | 1866 | 0.45% | 1532 | 40123 |
| SOL Up or Down 5m 21 Diff Up Progress | 32112 | 30824 | 50.13% | 272359 | 1817 | 0.67% | 1470 | 25962 |
| SOL Up or Down 5m 22 Diff Up Progress | 32044 | 30757 | 50.13% | 268740 | 1806 | 0.67% | 1454 | 25609 |
| SOL Up or Down 5m 23 Diff Up Progress | 32037 | 30750 | 50.14% | 265226 | 1800 | 0.68% | 1441 | 25266 |
| SOL Up or Down 5m 24 Diff Up Progress | 31985 | 30699 | 50.15% | 261723 | 1777 | 0.68% | 1432 | 24947 |
| SOL Up or Down 5m 25 Diff Up Progress | 31901 | 30616 | 50.16% | 258279 | 1733 | 0.67% | 1424 | 24648 |
| SOL Up or Down 5m 26 Diff Up Progress | 31894 | 30609 | 50.17% | 255026 | 1684 | 0.66% | 1420 | 24340 |
| SOL Up or Down 5m 13 Diff Up Progress | 45962 | 44190 | 50.09% | 416041 | 1677 | 0.4% | 1547 | 40735 |
| SOL Up or Down 5m 27 Diff Up Progress | 31492 | 30226 | 50.19% | 251501 | 1651 | 0.66% | 1413 | 24019 |

## Top Strategies By ROI (min 50 settled bets)

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ETH Up or Down 5m 38 Diff Down Progress | 1020 | 1015 | 51.92% | 1015 | 39 | 3.84% | 14 | 0 |
| BTC Up or Down 5m 37 Diff Up Progress | 14663 | 14640 | 50.26% | 108917 | 1349 | 1.24% | 679 | 9169 |
| BTC Up or Down 5m 38 Diff Up Progress | 14642 | 14620 | 50.27% | 106413 | 1289 | 1.21% | 670 | 9012 |
| BTC Up or Down 5m 46 Diff Up Progress | 13303 | 13281 | 50.18% | 88947 | 983 | 1.11% | 593 | 7273 |
| BTC Up or Down 5m 39 Diff Up Progress | 13406 | 13384 | 50.15% | 102444 | 1112 | 1.09% | 659 | 8858 |
| BTC Up or Down 5m 40 Diff Up Progress | 13397 | 13375 | 50.15% | 100336 | 1068 | 1.06% | 648 | 8680 |
| BTC Up or Down 5m 41 Diff Up Progress | 13390 | 13368 | 50.16% | 98312 | 1032 | 1.05% | 636 | 8484 |
| BTC Up or Down 5m 45 Diff Up Progress | 13316 | 13294 | 50.17% | 90795 | 945 | 1.04% | 603 | 7518 |
| BTC Up or Down 5m 44 Diff Up Progress | 13321 | 13299 | 50.17% | 92617 | 963 | 1.04% | 611 | 7774 |
| BTC Up or Down 5m 42 Diff Up Progress | 13333 | 13311 | 50.16% | 96314 | 994 | 1.03% | 622 | 8276 |
| BTC Up or Down 5m 43 Diff Up Progress | 13332 | 13310 | 50.17% | 94454 | 952 | 1.01% | 618 | 8030 |
| SOL Up or Down 5m 24 Diff Up Progress | 31985 | 30699 | 50.15% | 261723 | 1777 | 0.68% | 1432 | 24947 |
| SOL Up or Down 5m 23 Diff Up Progress | 32037 | 30750 | 50.14% | 265226 | 1800 | 0.68% | 1441 | 25266 |
| SOL Up or Down 5m 22 Diff Up Progress | 32044 | 30757 | 50.13% | 268740 | 1806 | 0.67% | 1454 | 25609 |
| SOL Up or Down 5m 25 Diff Up Progress | 31901 | 30616 | 50.16% | 258279 | 1733 | 0.67% | 1424 | 24648 |

## Worst Strategies By PnL

| Strategy | Entries | Settled | Win % | Stake | PnL | ROI | Max DD | Capped |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| ETH Up or Down 5m 4 Diff Down Progress | 47843 | 47726 | 49.88% | 459393 | -1151 | -0.25% | 2799 | 44579 |
| ETH Up or Down 5m 5 Diff Down Progress | 47685 | 47569 | 49.87% | 456907 | -1133 | -0.25% | 2788 | 44250 |
| ETH Up or Down 5m 3 Diff Down Progress | 47970 | 47853 | 49.87% | 461871 | -1129 | -0.24% | 2820 | 44859 |
| ETH Up or Down 5m 2 Diff Down Progress | 48096 | 47979 | 49.87% | 464428 | -1098 | -0.24% | 2836 | 45095 |
| ETH Up or Down 5m 6 Diff Down Progress | 47549 | 47433 | 49.87% | 454396 | -1096 | -0.24% | 2783 | 43861 |
| ETH Up or Down 5m 1 Diff Down Progress | 48238 | 48121 | 49.88% | 467055 | -1087 | -0.23% | 2846 | 45334 |
| BTC Up or Down 5m 5 Diff Down Progress | 48643 | 48574 | 49.92% | 476662 | -1062 | -0.22% | 2221 | 46996 |
| BTC Up or Down 5m 4 Diff Down Progress | 48752 | 48683 | 49.94% | 477817 | -1055 | -0.22% | 2233 | 47174 |
| BTC Up or Down 5m 6 Diff Down Progress | 48504 | 48435 | 49.91% | 475436 | -1020 | -0.21% | 2213 | 46796 |
| BTC Up or Down 5m 3 Diff Down Progress | 48915 | 48842 | 49.95% | 479115 | -1007 | -0.21% | 2249 | 47312 |
| BTC Up or Down 5m 7 Diff Down Progress | 48407 | 48338 | 49.9% | 474166 | -980 | -0.21% | 2207 | 46600 |
| BTC Up or Down 5m 2 Diff Down Progress | 48989 | 48915 | 49.94% | 480385 | -961 | -0.2% | 2262 | 47417 |
| BTC Up or Down 5m 8 Diff Down Progress | 48335 | 48266 | 49.89% | 472832 | -932 | -0.2% | 2195 | 46412 |
| ETH Up or Down 5m 33 Diff Down Progress | 8351 | 8333 | 49.53% | 73375 | -931 | -1.27% | 1447 | 7082 |
| BTC Up or Down 5m 1 Diff Down Progress | 49129 | 49054 | 49.96% | 481811 | -927 | -0.19% | 2274 | 47517 |

## Window

- BTC: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- ETH: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.
- SOL: 52230 evaluated candles, 2025-12-29T00:00:00.0000000+00:00 through 2026-06-28T08:25:00.0000000+00:00.

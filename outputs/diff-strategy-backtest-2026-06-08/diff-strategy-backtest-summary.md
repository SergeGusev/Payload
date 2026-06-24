# Diff Strategy Backtest

- Counter model: zero-start per asset at the first loaded historical market, dynamic `DiffCount` threshold +/-5.
- Entry signal: market `T` uses adjusted Diff after market `T-5m`; positive Diff buys `Down`, negative Diff buys `Up`.
- `instant` model: first available odds tick in the first 60 seconds, selected outcome `best_ask <= 0.65`, settled only when the terminal odds result is strong (`winner >= 0.80`, loser `<= 0.20`).
- `fixed05_strong` model: same strong terminal odds result, assumed entry price `0.50`.
- `fixed05_binance` model: assumed entry price `0.50`, settled by the final Binance move sign; this is a coverage/sensitivity check, not the primary Polymarket settlement proxy.

## Asset Summary

| Asset | Markets | Period UTC | Instant bets | Instant PnL | Instant ROI | Fixed 0.50 strong bets | Fixed 0.50 PnL | Fixed 0.50 ROI |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| BTC | 7799 | 2026-05-08 21:45..2026-06-08 20:20 | 13997 | -455.53087996 | -3.25448939% | 14494 | 44 | 0.30357389% |
| ETH | 5861 | 2026-05-09 10:15..2026-06-08 20:20 | 10354 | -381.58132682 | -3.68535181% | 10576 | 26 | 0.24583964% |
| SOL | 5856 | 2026-05-09 10:15..2026-06-08 20:20 | 12322 | -814.43146346 | -6.60957201% | 12798 | -358 | -2.79731208% |

## Top Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| ETH Up or Down 5m Up 4 Diff Instant | 382 | 208 | 54.45026178% | 25.96298694 | 6.79659344% | 0.51157068 |
| ETH Up or Down 5m Up 6 Diff Instant | 102 | 64 | 62.74509804% | 24.79689287 | 24.31067928% | 0.50735294 |
| ETH Up or Down 5m Up 5 Diff Instant | 223 | 124 | 55.60538117% | 20.33050455 | 9.11681818% | 0.51165919 |
| BTC Up or Down 5m Down 9 Diff Instant | 87 | 50 | 57.47126437% | 8.97277938 | 10.31353952% | 0.50965517 |
| ETH Up or Down 5m Up 7 Diff Instant | 58 | 34 | 58.62068966% | 8.90720479 | 15.35724963% | 0.50586207 |
| BTC Up or Down 5m Down 10 Diff Instant | 69 | 39 | 56.52173913% | 5.6135444 | 8.13557159% | 0.51072464 |
| ETH Up or Down 5m Up 8 Diff Instant | 27 | 16 | 59.25925926% | 4.92303349 | 18.23345737% | 0.50740741 |
| SOL Up or Down 5m Down 9 Diff Instant | 84 | 45 | 53.57142857% | 3.61959691 | 4.30904394% | 0.51 |
| SOL Up or Down 5m Down 10 Diff Instant | 66 | 36 | 54.54545455% | 3.06608028 | 4.64557618% | 0.51030303 |
| BTC Up or Down 5m Down 8 Diff Instant | 115 | 60 | 52.17391304% | 1.58548452 | 1.37868219% | 0.50756522 |
| ETH Up or Down 5m Down 8 Diff Instant | 30 | 16 | 53.33333333% | 0.74074832 | 2.46916106% | 0.51833333 |
| BTC Up or Down 5m Up 10 Diff Instant | 68 | 33 | 48.52941176% | -2.71340568 | -3.99030247% | 0.50455882 |
| BTC Up or Down 5m Down 7 Diff Instant | 160 | 80 | 50% | -3.2522035 | -2.03262719% | 0.50925 |
| ETH Up or Down 5m Down 7 Diff Instant | 74 | 37 | 50% | -3.34400922 | -4.51893138% | 0.51932432 |
| ETH Up or Down 5m Down 6 Diff Instant | 129 | 66 | 51.1627907% | -3.72100062 | -2.8844966% | 0.52069767 |

## Worst Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| SOL Up or Down 5m Up 1 Diff Instant | 2212 | 1056 | 47.73960217% | -149.57703245 | -6.76207199% | 0.51379295 |
| ETH Up or Down 5m Down 1 Diff Instant | 2208 | 1057 | 47.87137681% | -142.77555623 | -6.46628425% | 0.51091486 |
| SOL Up or Down 5m Down 1 Diff Instant | 2203 | 1062 | 48.20699047% | -130.40076556 | -5.91923584% | 0.51197458 |
| ETH Up or Down 5m Down 2 Diff Instant | 1329 | 635 | 47.78028593% | -100.4625993 | -7.55926255% | 0.51548533 |
| SOL Up or Down 5m Up 2 Diff Instant | 1428 | 680 | 47.61904762% | -99.91539686 | -6.99687653% | 0.51472689 |
| SOL Up or Down 5m Down 2 Diff Instant | 1400 | 673 | 48.07142857% | -89.36251465 | -6.38303676% | 0.51335714 |
| BTC Up or Down 5m Down 2 Diff Instant | 1676 | 818 | 48.80668258% | -83.753129 | -4.9972034% | 0.51113962 |
| ETH Up or Down 5m Down 3 Diff Instant | 786 | 369 | 46.94656489% | -75.4940072 | -9.60483552% | 0.51763359 |
| BTC Up or Down 5m Up 2 Diff Instant | 1709 | 832 | 48.68344061% | -73.0932279 | -4.27695892% | 0.50748976 |
| SOL Up or Down 5m Down 3 Diff Instant | 895 | 422 | 47.15083799% | -69.7838593 | -7.79707925% | 0.51365363 |
| BTC Up or Down 5m Down 1 Diff Instant | 2797 | 1384 | 49.48158742% | -66.62898406 | -2.38215889% | 0.50676439 |
| ETH Up or Down 5m Up 1 Diff Instant | 2222 | 1116 | 50.2250225% | -45.81319143 | -2.06179979% | 0.51132313 |
| SOL Up or Down 5m Up 3 Diff Instant | 895 | 434 | 48.49162011% | -45.17698871 | -5.04770824% | 0.51536313 |
| BTC Up or Down 5m Down 4 Diff Instant | 511 | 245 | 47.94520548% | -39.54105659 | -7.73797585% | 0.51587084 |
| SOL Up or Down 5m Down 4 Diff Instant | 557 | 266 | 47.75583483% | -38.00239522 | -6.82269214% | 0.51576302 |

## Top Fixed 0.50 Strong Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m Up 1 Diff Instant | 2868 | 1458 | 50.83682008% | 48 | 1.67364017% | 0.5 |
| ETH Up or Down 5m Up 4 Diff Instant | 389 | 214 | 55.01285347% | 39 | 10.02570694% | 0.5 |
| ETH Up or Down 5m Up 2 Diff Instant | 1366 | 699 | 51.17130307% | 32 | 2.34260615% | 0.5 |
| ETH Up or Down 5m Up 6 Diff Instant | 105 | 67 | 63.80952381% | 29 | 27.61904762% | 0.5 |
| ETH Up or Down 5m Up 5 Diff Instant | 226 | 127 | 56.19469027% | 28 | 12.38938053% | 0.5 |
| ETH Up or Down 5m Up 1 Diff Instant | 2271 | 1146 | 50.46235139% | 21 | 0.92470277% | 0.5 |
| BTC Up or Down 5m Down 9 Diff Instant | 91 | 54 | 59.34065934% | 17 | 18.68131868% | 0.5 |
| BTC Up or Down 5m Down 1 Diff Instant | 2917 | 1466 | 50.25711347% | 15 | 0.51422695% | 0.5 |
| BTC Up or Down 5m Down 10 Diff Instant | 73 | 43 | 58.90410959% | 13 | 17.80821918% | 0.5 |
| BTC Up or Down 5m Down 3 Diff Instant | 1005 | 509 | 50.64676617% | 13 | 1.29353234% | 0.5 |
| ETH Up or Down 5m Up 7 Diff Instant | 60 | 36 | 60% | 12 | 20% | 0.5 |
| SOL Up or Down 5m Down 10 Diff Instant | 69 | 39 | 56.52173913% | 9 | 13.04347826% | 0.5 |
| SOL Up or Down 5m Down 9 Diff Instant | 87 | 48 | 55.17241379% | 9 | 10.34482759% | 0.5 |
| BTC Up or Down 5m Down 8 Diff Instant | 121 | 65 | 53.71900826% | 9 | 7.43801653% | 0.5 |
| BTC Up or Down 5m Down 5 Diff Instant | 337 | 173 | 51.33531157% | 9 | 2.67062315% | 0.5 |

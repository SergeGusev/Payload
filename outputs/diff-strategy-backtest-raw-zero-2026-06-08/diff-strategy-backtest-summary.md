# Diff Strategy Backtest

- Counter model: zero-start per asset at the first loaded historical market, raw `Diff = UpCount - DownCount` without dynamic zero adjustment.
- Entry signal: market `T` uses raw Diff after market `T-5m`; positive Diff buys `Down`, negative Diff buys `Up`.
- `instant` model: first available odds tick in the first 60 seconds, selected outcome `best_ask <= 0.65`, settled only when the terminal odds result is strong (`winner >= 0.80`, loser `<= 0.20`).
- `fixed05_strong` model: same strong terminal odds result, assumed entry price `0.50`.
- `fixed05_binance` model: assumed entry price `0.50`, settled by the final Binance move sign; this is a coverage/sensitivity check, not the primary Polymarket settlement proxy.

## Asset Summary

| Asset | Markets | Period UTC | Instant bets | Instant PnL | Instant ROI | Fixed 0.50 strong bets | Fixed 0.50 PnL | Fixed 0.50 ROI |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| BTC | 7802 | 2026-05-08 21:45..2026-06-08 20:35 | 84907 | 261.79460223 | 0.308331% | 87819 | 1017 | 1.15806374% |
| ETH | 5864 | 2026-05-09 10:15..2026-06-08 20:35 | 81451 | -2940.62888389 | -3.61030421% | 83554 | -1290 | -1.54391172% |
| SOL | 5859 | 2026-05-09 10:15..2026-06-08 20:35 | 56906 | -137.4901335 | -0.2416092% | 58930 | 1150 | 1.95146784% |

## Top Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m Up 35 Diff Instant | 1784 | 930 | 52.13004484% | 81.40437389 | 4.56302544% | 0.49720852 |
| BTC Up or Down 5m Up 40 Diff Instant | 1114 | 581 | 52.15439856% | 52.78666336 | 4.73847965% | 0.49545781 |
| BTC Up or Down 5m Up 30 Diff Instant | 2494 | 1262 | 50.60144346% | 41.59275811 | 1.66771284% | 0.49678428 |
| BTC Up or Down 5m Up 45 Diff Instant | 584 | 304 | 52.05479452% | 36.20747112 | 6.19990944% | 0.48844178 |
| SOL Up or Down 5m Up 9 Diff Instant | 2014 | 1035 | 51.39026812% | 27.48526614 | 1.36471033% | 0.50886296 |
| BTC Up or Down 5m Up 55 Diff Instant | 308 | 159 | 51.62337662% | 23.39878808 | 7.59700912% | 0.48136364 |
| SOL Up or Down 5m Up 20 Diff Instant | 1008 | 519 | 51.48809524% | 23.34705486 | 2.31617608% | 0.50510913 |
| BTC Up or Down 5m Up 50 Diff Instant | 426 | 217 | 50.93896714% | 23.03062523 | 5.40625005% | 0.48368545 |
| SOL Up or Down 5m Up 40 Diff Instant | 413 | 219 | 53.02663438% | 22.49598516 | 5.44696977% | 0.50009685 |
| BTC Up or Down 5m Up 60 Diff Instant | 161 | 87 | 54.03726708% | 20.73974591 | 12.88182976% | 0.48335404 |
| SOL Up or Down 5m Up 15 Diff Instant | 1355 | 696 | 51.36531365% | 20.50718244 | 1.5134452% | 0.50788192 |
| SOL Up or Down 5m Up 45 Diff Instant | 207 | 114 | 55.07246377% | 19.17473269 | 9.26315589% | 0.50154589 |
| BTC Up or Down 5m Down 70 Diff Instant | 177 | 96 | 54.23728814% | 18.28988577 | 10.3332688% | 0.48864407 |
| BTC Up or Down 5m Up 5 Diff Instant | 3657 | 1836 | 50.20508614% | 18.25738174 | 0.49924478% | 0.49796008 |
| ETH Up or Down 5m Up 30 Diff Instant | 143 | 83 | 58.04195804% | 17.99727206 | 12.58550494% | 0.50965035 |

## Worst Instant Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| ETH Up or Down 5m Down 2 Diff Instant | 3682 | 1775 | 48.20749593% | -172.19512627 | -4.67667372% | 0.50466594 |
| ETH Up or Down 5m Down 3 Diff Instant | 3617 | 1744 | 48.21675422% | -167.87615265 | -4.64130917% | 0.50452309 |
| ETH Up or Down 5m Down 1 Diff Instant | 3753 | 1814 | 48.3346656% | -166.97190049 | -4.44902479% | 0.50475353 |
| ETH Up or Down 5m Down 4 Diff Instant | 3552 | 1712 | 48.1981982% | -165.22722833 | -4.65166746% | 0.5044116 |
| ETH Up or Down 5m Down 6 Diff Instant | 3398 | 1638 | 48.20482637% | -159.12887086 | -4.68301562% | 0.50455268 |
| ETH Up or Down 5m Down 8 Diff Instant | 3253 | 1567 | 48.17091915% | -156.39119096 | -4.80759886% | 0.50458654 |
| ETH Up or Down 5m Down 7 Diff Instant | 3326 | 1604 | 48.22609741% | -155.94736509 | -4.68873617% | 0.50461515 |
| ETH Up or Down 5m Down 5 Diff Instant | 3477 | 1680 | 48.3175151% | -153.21338074 | -4.40648205% | 0.50438309 |
| ETH Up or Down 5m Down 10 Diff Instant | 3079 | 1485 | 48.22994479% | -147.5126381 | -4.79092686% | 0.50479052 |
| ETH Up or Down 5m Down 9 Diff Instant | 3169 | 1531 | 48.31177027% | -143.74202071 | -4.53587948% | 0.50474282 |
| ETH Up or Down 5m Down 15 Diff Instant | 2549 | 1224 | 48.01883091% | -129.4207143 | -5.07731323% | 0.50399765 |
| ETH Up or Down 5m Down 25 Diff Instant | 1840 | 863 | 46.90217391% | -117.33421957 | -6.37685976% | 0.50080435 |
| ETH Up or Down 5m Down 30 Diff Instant | 1755 | 822 | 46.83760684% | -112.55664726 | -6.41348417% | 0.50048433 |
| ETH Up or Down 5m Down 20 Diff Instant | 2047 | 976 | 47.67953102% | -109.6349919 | -5.35588627% | 0.50285296 |
| ETH Up or Down 5m Down 35 Diff Instant | 1723 | 807 | 46.83691236% | -109.15653046 | -6.33526004% | 0.5003018 |

## Top Fixed 0.50 Strong Strategies By PnL

| Strategy | Bets | Wins | Win Rate | PnL | ROI | Avg Entry |
|---|---:|---:|---:|---:|---:|---:|
| BTC Up or Down 5m Up 35 Diff Instant | 1822 | 961 | 52.7442371% | 100 | 5.4884742% | 0.5 |
| SOL Up or Down 5m Up 4 Diff Instant | 2695 | 1389 | 51.53988868% | 83 | 3.07977737% | 0.5 |
| SOL Up or Down 5m Up 9 Diff Instant | 2071 | 1074 | 51.85900531% | 77 | 3.71801062% | 0.5 |
| BTC Up or Down 5m Up 1 Diff Instant | 4248 | 2159 | 50.82391714% | 70 | 1.64783427% | 0.5 |
| SOL Up or Down 5m Up 10 Diff Instant | 1926 | 997 | 51.76531672% | 68 | 3.53063344% | 0.5 |
| BTC Up or Down 5m Up 2 Diff Instant | 4111 | 2089 | 50.81488689% | 67 | 1.62977378% | 0.5 |
| SOL Up or Down 5m Up 8 Diff Instant | 2214 | 1140 | 51.49051491% | 66 | 2.98102981% | 0.5 |
| SOL Up or Down 5m Up 3 Diff Instant | 2839 | 1450 | 51.07432194% | 61 | 2.14864389% | 0.5 |
| BTC Up or Down 5m Up 5 Diff Instant | 3739 | 1900 | 50.81572613% | 61 | 1.63145226% | 0.5 |
| BTC Up or Down 5m Up 4 Diff Instant | 3861 | 1961 | 50.78995079% | 61 | 1.57990158% | 0.5 |
| BTC Up or Down 5m Up 3 Diff Instant | 3983 | 2022 | 50.76575446% | 61 | 1.53150891% | 0.5 |
| BTC Up or Down 5m Up 30 Diff Instant | 2548 | 1304 | 51.17739403% | 60 | 2.35478807% | 0.5 |
| SOL Up or Down 5m Up 7 Diff Instant | 2338 | 1198 | 51.24037639% | 58 | 2.48075278% | 0.5 |
| BTC Up or Down 5m Up 40 Diff Instant | 1133 | 594 | 52.42718447% | 55 | 4.85436893% | 0.5 |
| SOL Up or Down 5m Up 6 Diff Instant | 2451 | 1253 | 51.12199102% | 55 | 2.24398205% | 0.5 |
